using ComelitApiGateway.Commons.Dtos.Vedo;
using ComelitApiGateway.Commons.Dtos.Vedo.ComelitSystem;
using ComelitApiGateway.Commons.Enums.Vedo;
using ComelitApiGateway.Commons.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ComelitApiGateway.Services;

public class ComelitVedoService(
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    ILogger<ComelitVedoService> logger) : IComelitVedo
{
    private const int MaxLoginRetries = 5;

    private static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

    // Serializes login so concurrent requests don't trigger overlapping logins.
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    // Serializes the lazy initialization of the cached areas and the firmware detection.
    private readonly SemaphoreSlim _checkFirmwareVersionLock = new(1, 1);

    #region Cache

    // Built once and reused. The reference is assigned atomically; callers read the current
    // reference, there is no in-place mutation of a shared list.
    private List<VedoAreaDTO>? _areas;

    // Detected once and cached: VEDO panels expose two different /action.cgi dialects.
    private VedoFirmwareVersion? _firmwareVersion;

    #endregion



    # region Api call wrapper

    // Outcome of an authenticated request, after the session-expiry retry has been resolved.
    private readonly record struct VedoResponse(HttpStatusCode StatusCode, bool IsSuccessStatusCode, string Body);

    // Sends an authenticated request and transparently re-logs in and retries when the panel
    // reports the session is missing/expired. Every CGI endpoint (GET status, GET/POST action)
    // returns a "Not logged" body in that case regardless of the HTTP status code, so this is
    // the single, reliable signal: detect it by content. This covers first login, server-side
    // session expiry (newer firmware) and handler rotation uniformly.
    private async Task<VedoResponse> SendRequestAndRetryIfNotAuthenticated(
        Func<HttpClient, CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken,
        int retryCount = 0)
    {
        var client = CreateHttpClient();
        using var response = await send(client, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (body.Contains("Not logged"))
        {
            if (retryCount >= MaxLoginRetries)
            {
                logger.LogError("Login retry limit ({Max}) reached.", MaxLoginRetries);
                throw new UnauthorizedAccessException($"Unable to authenticate to the VEDO panel after {MaxLoginRetries} attempts.");
            }

            logger.LogWarning("Session expired, re-login attempt {Attempt}.", retryCount + 1);
            await LoginAsync(cancellationToken);
            await Task.Delay(500, cancellationToken);
            return await SendRequestAndRetryIfNotAuthenticated(send, cancellationToken, retryCount + 1);
        }

        return new VedoResponse(response.StatusCode, response.IsSuccessStatusCode, body);
    }

    private async Task<T?> ComelitApiGetCallAsync<T>(string apiUrl, CancellationToken cancellationToken) where T : class
    {
        var result = await SendRequestAndRetryIfNotAuthenticated(
            (client, ct) => client.GetAsync($"{apiUrl}?_={GenerateAntiCacheToken()}", ct),
            cancellationToken);

        if (!result.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"VEDO panel returned {(int)result.StatusCode} calling {apiUrl}.");
        }

        return JsonSerializer.Deserialize<T>(result.Body, JsonWebOptions);
    }

    private async Task<bool> ComelitApiActionCallAsync(Dictionary<string, string> @params, CancellationToken cancellationToken)
    {
        var url = new StringBuilder($"/action.cgi?_={GenerateAntiCacheToken()}");
        foreach (var p in @params)
        {
            url.Append($"&{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}");
        }

        var result = await SendRequestAndRetryIfNotAuthenticated((client, ct) => client.GetAsync(url.ToString(), ct), cancellationToken);
        return result.IsSuccessStatusCode;
    }

    private async Task<bool> ComelitApiPostCallAsync(Dictionary<string, string> @params, CancellationToken cancellationToken)
    {
        // FormUrlEncodedContent is rebuilt on every attempt: HttpContent cannot be resent.
        var result = await SendRequestAndRetryIfNotAuthenticated(
            (client, ct) => client.PostAsync("/action.cgi", new FormUrlEncodedContent(@params), ct),
            cancellationToken);

        // Depending on the firmware, the VEDO panel may reply 404 even when the action succeeds,
        // while other firmwares reply 200. Treat both as success (same behaviour as aiocomelit).
        return result.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound;
    }
    
    #endregion

    #region Vedo functionality

    public async Task<string> LoginAsync(CancellationToken cancellationToken = default)
    {
        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            var client = CreateHttpClient();
            using var response = await client.PostAsync(
                "/login.cgi",
                new StringContent($"code={config["VEDO_KEY"]}&_={GenerateAntiCacheToken()}"),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            // The session cookie (Set-Cookie: uid=...) is stored automatically by the
            // CookieContainer on the shared handler and resent on the next requests.
            // It is returned here only for informational/backward-compatible purposes.
            return response.Headers.TryGetValues("Set-Cookie", out var cookies)
                ? cookies.FirstOrDefault() ?? string.Empty
                : string.Empty;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    public async Task<AreaStatusResponseDTO> ComelitGetAreasStatusAsync(CancellationToken cancellationToken = default)
    {
        return (await ComelitApiGetCallAsync<AreaStatusResponseDTO>("/user/area_stat.json", cancellationToken)) ?? new AreaStatusResponseDTO();
    }

    public async Task<AreaDescriptionResponseDTO> ComelitGetAreasDescriptionAsync(CancellationToken cancellationToken = default)
    {
        return (await ComelitApiGetCallAsync<AreaDescriptionResponseDTO>("/user/area_desc.json", cancellationToken)) ?? new AreaDescriptionResponseDTO();
    }

    private async Task<ZoneDescriptionResponseDTO> ComelitGetZonesDescriptionAsync(CancellationToken cancellationToken = default)
    {
        return (await ComelitApiGetCallAsync<ZoneDescriptionResponseDTO>("/user/zone_desc.json", cancellationToken)) ?? new ZoneDescriptionResponseDTO();
    }

    public async Task<ZoneStatusResponseDTO> ComelitGetZonesStatusAsync(CancellationToken cancellationToken = default)
    {
        return (await ComelitApiGetCallAsync<ZoneStatusResponseDTO>("/user/zone_stat.json", cancellationToken)) ?? new ZoneStatusResponseDTO();
    }

    public async Task<List<VedoAreaDTO>> GetAreasListAsync(CancellationToken cancellationToken = default)
    {
        if (_areas is not null) return _areas;

        await _checkFirmwareVersionLock.WaitAsync(cancellationToken);
        try
        {
            if (_areas is not null) return _areas;

            var excludedAreas = config["VEDO_EXCLUDED_AREAS_ID"]?.ToString() ?? "";
            var elements = (await ComelitGetAreasDescriptionAsync(cancellationToken)).AreaNames;

            var areas = new List<VedoAreaDTO>();
            for (int i = 0; i < elements.Count; i++)
            {
                if (!excludedAreas.Split(",").Any(x => x == i.ToString()))
                {
                    areas.Add(new VedoAreaDTO()
                    {
                        Description = elements[i],
                        Id = i
                    });
                }
            }

            // Atomic publish of the fully built list.
            _areas = areas;
            return _areas;
        }
        finally
        {
            _checkFirmwareVersionLock.Release();
        }
    }

    public async Task<List<VedoAreaStatusDTO>> GetAreasStatusAsync(CancellationToken cancellationToken = default)
    {
        var areas = await GetAreasListAsync(cancellationToken);
        var status = await ComelitGetAreasStatusAsync(cancellationToken);

        // Guard against malformed responses whose arrays are shorter than the area count.
        int available = new[]
        {
            status.ArmedAreas.Count,
            status.OutTimeAreas.Count,
            status.InTimeAreas.Count,
            status.AlarmedAreas.Count,
            status.LastAlarmedAreas.Count,
            status.AnomalyAreas.Count,
            status.ReadyAreas.Count
        }.Min();

        var result = new List<VedoAreaStatusDTO>(areas.Count);
        for (int i = 0; i < areas.Count; i++)
        {
            var areaStatus = new VedoAreaStatusDTO()
            {
                Id = areas[i].Id,
                Description = areas[i].Description
            };

            if (i < available)
            {
                areaStatus.Armed = status.ArmedAreas[i] != 0 && status.OutTimeAreas[i] == 0;
                areaStatus.InTime = status.InTimeAreas[i] != 0;
                areaStatus.OutTime = status.OutTimeAreas[i] != 0;
                areaStatus.Alarm = status.AlarmedAreas[i] != 0;
                areaStatus.AlarmMemory = status.LastAlarmedAreas[i] != 0;
                areaStatus.Anomaly = status.AnomalyAreas[i] != 0;
                areaStatus.Ready = status.ReadyAreas[i] != 0;

                if (areaStatus.Alarm)
                {
                    areaStatus.Status = AlarmStatusEnum.Alarm;
                }
                else if (areaStatus.OutTime)
                {
                    areaStatus.Status = AlarmStatusEnum.Activating;
                }
                else if (areaStatus.Armed)
                {
                    areaStatus.Status = AlarmStatusEnum.Active;
                }
                else
                {
                    areaStatus.Status = AlarmStatusEnum.NotEntered;
                }
            }

            result.Add(areaStatus);
        }

        return result;
    }

    public async Task<List<VedoZoneDTO>> GetZoneListAsync(int idArea = -1, bool removeHiddenZones = true, CancellationToken cancellationToken = default)
    {
        //If you call this endpoint without call "GetAreaList" you can see all the hidden devices
        await GetAreasListAsync(cancellationToken);
        var response = await ComelitGetZonesDescriptionAsync(cancellationToken);
        var responseStatus = await ComelitGetZonesStatusAsync(cancellationToken);

        var zones = new List<VedoZoneDTO>();
        for (int i = 0; i < response.ZoneNames.Count; i++)
        {
            if (!String.IsNullOrEmpty(response.ZoneNames[i]))
            {
                var zone = new VedoZoneDTO()
                {
                    Id = i,
                    Description = response.ZoneNames[i],
                    AreaId = response.InArea[i] - 1,
                    Hidden = response.Present.Substring(i, 1) == "0"
                };

                var radix16Number = Convert.ToInt32(responseStatus.ZoneStatus.Split(",")[i], 16);
                if (radix16Number == (int)VedoZoneStatusEnum.Ready)
                {
                    zone.Status = VedoZoneStatusEnum.Ready;
                    zone.StatusDescription = VedoZoneStatusEnum.Ready.ToString();
                }
                else if (radix16Number == (int)VedoZoneStatusEnum.Active)
                {
                    zone.Status = VedoZoneStatusEnum.Ready;
                    zone.StatusDescription = VedoZoneStatusEnum.Ready.ToString();
                }
                //else if ((radix16Number & (int)VedoZoneStatusEnum.Open) == 1)
                else if ((radix16Number == (int)VedoZoneStatusEnum.Open) || ((radix16Number & (int)VedoZoneStatusEnum.Open) == 1))
                {
                    zone.Status = VedoZoneStatusEnum.Open;
                    zone.StatusDescription = VedoZoneStatusEnum.Open.ToString();
                }
                else if (radix16Number == (int)VedoZoneStatusEnum.Isolated)
                {
                    zone.Status = VedoZoneStatusEnum.Isolated;
                    zone.StatusDescription = VedoZoneStatusEnum.Isolated.ToString();
                }
                else if (radix16Number == (int)VedoZoneStatusEnum.Sabotated)
                {
                    zone.Status = VedoZoneStatusEnum.Sabotated;
                    zone.StatusDescription = VedoZoneStatusEnum.Sabotated.ToString();
                }
                else if (radix16Number == (int)VedoZoneStatusEnum.Inhibited)
                {
                    zone.Status = VedoZoneStatusEnum.Inhibited;
                    zone.StatusDescription = VedoZoneStatusEnum.Inhibited.ToString();
                }
                else if (radix16Number == (int)VedoZoneStatusEnum.Excluded)
                {
                    zone.Status = VedoZoneStatusEnum.Excluded;
                    zone.StatusDescription = VedoZoneStatusEnum.Excluded.ToString();
                }
                else
                {
                    zone.Status = VedoZoneStatusEnum.Unknown;
                    zone.StatusDescription = VedoZoneStatusEnum.Unknown.ToString();
                }

                zones.Add(zone);
            }
        }

        if (removeHiddenZones) zones = zones.Where(x => !x.Hidden).ToList();

        return zones.Where(x => idArea == -1 || x.AreaId == idArea).ToList();
    }

    public async Task<bool> ArmAlarmAsync(int? area = null, bool force = true, CancellationToken cancellationToken = default)
    {
        return await SetAreaStatusAsync("tot", area, force, cancellationToken);
    }

    public async Task<bool> DisarmAlarmAsync(int? area = null, bool force = true, CancellationToken cancellationToken = default)
    {
        return await SetAreaStatusAsync("dis", area, force, cancellationToken);
    }

    public async Task<bool> ExcludeZoneAsync(int zoneId, CancellationToken cancellationToken = default)
    {
        return await ComelitApiActionCallAsync(new Dictionary<string, string>() {
            { "vedo", "1" },
            { "excl", zoneId.ToString() }
        }, cancellationToken);
    }

    public async Task<bool> IncludeZoneAsync(int zoneId, CancellationToken cancellationToken = default)
    {
        return await ComelitApiActionCallAsync(new Dictionary<string, string>() {
            { "vedo", "1" },
            { "incl", zoneId.ToString() }
        }, cancellationToken);
    }

    public async Task<bool> IsolateZoneAsync(int zoneId, CancellationToken cancellationToken = default)
    {
        return await ComelitApiActionCallAsync(new Dictionary<string, string>() {
            { "vedo", "1" },
            { "isol", zoneId.ToString() }
        }, cancellationToken);
    }

    public async Task<bool> UnisolateZoneAsync(int zoneId, CancellationToken cancellationToken = default)
    {
        return await ComelitApiActionCallAsync(new Dictionary<string, string>() {
            { "vedo", "1" },
            { "activ", zoneId.ToString() }
        }, cancellationToken);
    }

    #endregion

    #region private

    private async Task<VedoFirmwareVersion> GetFirmwareVersionAsync(CancellationToken cancellationToken)
    {
        if (_firmwareVersion.HasValue) return _firmwareVersion.Value;

        await _checkFirmwareVersionLock.WaitAsync(cancellationToken);
        try
        {
            // check again after semaphore wait
            if (_firmwareVersion.HasValue) return _firmwareVersion.Value;

            // Manual override via configuration (v1 / v2), otherwise auto-detect.
            var configured = config["VEDO_API_VERSION_OVERRIDE"]?.Trim().ToLowerInvariant();
            if (configured == "v1") return (_firmwareVersion = VedoFirmwareVersion.V1).Value;
            if (configured == "v2") return (_firmwareVersion = VedoFirmwareVersion.V2).Value;

            // The v2 firmware web UI references the Comelit group site in index.shtml.
            var client = CreateHttpClient();
            using var response = await client.GetAsync("/index.shtml", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _firmwareVersion = body.Contains("www.comelitgroup.com") ? VedoFirmwareVersion.V2 : VedoFirmwareVersion.V1;
            logger.LogInformation("Detected VEDO firmware dialect: {Version}.", _firmwareVersion);
            return _firmwareVersion.Value;
        }
        finally
        {
            _checkFirmwareVersionLock.Release();
        }
    }

    // Arm ("tot") or disarm ("dis") an area, picking the call style for the detected firmware.
    private async Task<bool> SetAreaStatusAsync(string action, int? area, bool force, CancellationToken cancellationToken)
    {
        var areaParam = area?.ToString() ?? "32"; // 32 = all the areas

        if (await GetFirmwareVersionAsync(cancellationToken) == VedoFirmwareVersion.V2)
        {
            return await ComelitApiPostCallAsync(new Dictionary<string, string>() {
                { "forced", force ? "1" : "0" },
                { "vedo_param", "1" },
                { "type_param", action },
                { "area_param", areaParam }
            }, cancellationToken);
        }

        return await ComelitApiActionCallAsync(new Dictionary<string, string>() {
            { "force", force ? "1" : "0" },
            { "vedo", "1" },
            { action, areaParam }
        }, cancellationToken);
    }

    private HttpClient CreateHttpClient() => httpClientFactory.CreateClient("vedo");

    private static string GenerateAntiCacheToken() => DateTimeOffset.UtcNow.ToString("ddMMyyyyHHmmss");

    #endregion
}
