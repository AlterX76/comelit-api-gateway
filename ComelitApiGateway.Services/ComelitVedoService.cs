using ComelitApiGateway.Commons.Dtos.Vedo;
using ComelitApiGateway.Commons.Dtos.Vedo.ComelitSystem;
using ComelitApiGateway.Commons.Enums.Vedo;
using ComelitApiGateway.Commons.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using static System.Net.WebRequestMethods;

namespace ComelitApiGateway.Services
{
    public class ComelitVedoService : IComelitVedo
    {
        protected readonly IConfiguration _config;
        //Base address of comelit vedo
        private static string _BASE_ADDRESS = "";
        //Auth coockie for requests
        protected string _UID = "";
        private int loginRetryCounter = 0;

        #region Cache

        private static List<VedoAreaDTO> Areas = new();
        private static List<VedoAreaStatusDTO> AreaStatus = new();
        private static List<VedoZoneDTO> Zones = new();

        #endregion
        public ComelitVedoService(IConfiguration config)
        {
            _config = config;
            _BASE_ADDRESS = config["VEDO_URL"]?.ToString() ?? "";
        }

        public async Task<string> Login()
        {
            using (HttpClient client = await BuildHttpClient(true))
            {
                HttpResponseMessage response = await client.PostAsync("/login.cgi", new StringContent($"code={_config["VEDO_KEY"]}&_={DateTime.Now.ToString("ddMMyyyyhhmmss")}"));
                return SetCookieUID(response);
            }
        }

        #region Vedo System API Calls

        public async Task<T?> ComelitApiGetCall<T>(string apiUrl) where T : class
        {
            T? status;
            using (var client = await BuildHttpClient())
            {
                var response = await client.GetAsync($"{apiUrl}?_={DateTime.Now.ToString("ddMMyyyyhhmmss")}");
                var responseString = await response.Content.ReadAsStringAsync();

                if (responseString.Contains("Not logged"))
                {
                    await Login();
                    loginRetryCounter++;
                    Thread.Sleep(1000);

                    if (loginRetryCounter > 5) throw new UnauthorizedAccessException();

                    return await ComelitApiGetCall<T>(apiUrl);

                }

                loginRetryCounter = 0;
                status = await response.Content.ReadFromJsonAsync<T>();
            }

            return status;
        }

        public async Task<bool> ComelitApiActionCall(Dictionary<string, string> @params)
        {
            using (var client = await BuildHttpClient())
            {
                string url = $"/action.cgi?_={DateTime.Now.ToString("ddMMyyyyhhmmss")}";
                foreach (var p in @params)
                {
                    url += $"&{p.Key}={p.Value}";
                }

                var response = await client.GetAsync($"{url}");

                if (!response.IsSuccessStatusCode) return false;
                return true;
            }
        }

        public async Task<bool> ComelitApiPostCall(Dictionary<string, string> @params)
        {
            using (var client = await BuildHttpClient())
            {               
                var content = new FormUrlEncodedContent(@params);
                var response = await client.PostAsync($"/action.cgi", content);

                // this call return 404 even if the action is executed successfully, so i check only if the status code is different from 404
                if (response.StatusCode != HttpStatusCode.NotFound) return false;
                return true;
            }
        }

        public async Task<AreaStatusResponseDTO> ComelitGetAreasStatus()
        {
            return (await ComelitApiGetCall<AreaStatusResponseDTO>("/user/area_stat.json")) ?? new AreaStatusResponseDTO();
        }

        public async Task<AreaDescriptionResponseDTO> ComelitGetAreasDescription()
        {
            return (await ComelitApiGetCall<AreaDescriptionResponseDTO>("/user/area_desc.json")) ?? new AreaDescriptionResponseDTO();
        }

        public async Task<ZoneDescriptionResponseDTO> ComelitGetZonesDescription()
        {
            return (await ComelitApiGetCall<ZoneDescriptionResponseDTO>("/user/zone_desc.json")) ?? new ZoneDescriptionResponseDTO();
        }

        public async Task<ZoneStatusResponseDTO> ComelitGetZonesStatus()
        {
            return (await ComelitApiGetCall<ZoneStatusResponseDTO>("/user/zone_stat.json")) ?? new ZoneStatusResponseDTO();
        }



        #endregion

        #region Api call wrapper

        public async Task<List<VedoAreaDTO>> GetAreasList()
        {

            if (!Areas.Any())
            {
                var exludedAreas = _config["VEDO_EXCLUDED_AREAS_ID"]?.ToString() ?? "";
                var elements = (await ComelitGetAreasDescription()).AreaNames;
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!exludedAreas.Split(",").Any(x => x == i.ToString()))
                    {
                        Areas.Add(new VedoAreaDTO()
                        {
                            Description = elements[i],
                            Id = i
                        });
                    }
                }

            }

            return Areas;

        }

        public async Task<List<VedoAreaStatusDTO>> GetAreasStatus()
        {
            if (!Areas.Any())
            {
                await GetAreasList();
            }

            //If cached list is Empty i need to initialize
            if (!AreaStatus.Any())
            {
                Areas.ForEach(area =>
                {
                    AreaStatus.Add(new VedoAreaStatusDTO()
                    {
                        Id = area.Id,
                        Description = area.Description
                    });
                });
            }

            //For each area status I fill the object properties
            var status = await ComelitGetAreasStatus();
            for (int i = 0; i < Areas.Count; i++)
            {
                AreaStatus[i].Armed = status.ArmedAreas[i] != 0 && status.OutTimeAreas[i] == 0;
                AreaStatus[i].InTime = status.InTimeAreas[i] != 0;
                AreaStatus[i].OutTime = status.OutTimeAreas[i] != 0;
                AreaStatus[i].Alarm = status.AlarmedAreas[i] != 0;
                AreaStatus[i].AlarmMemory = status.LastAlarmedAreas[i] != 0;
                AreaStatus[i].Anomaly = status.AnomalyAreas[i] != 0;
                AreaStatus[i].Ready = status.ReadyAreas[i] != 0;

                if (AreaStatus[i].Alarm)
                {
                    AreaStatus[i].Status = AlarmStatusEnum.Alarm;
                }
                else if (AreaStatus[i].OutTime)
                {
                    AreaStatus[i].Status = AlarmStatusEnum.Activating;
                }
                else if (AreaStatus[i].Armed)
                {
                    AreaStatus[i].Status = AlarmStatusEnum.Active;
                }
                else
                {
                    AreaStatus[i].Status = AlarmStatusEnum.NotEntered;
                }
            }

            return AreaStatus;

        }

        public async Task<List<VedoZoneDTO>> GetZoneList(int idArea = -1, bool removeHiddenZones = true)
        {
            //If you call this endpoint without call "GetAreaList" you can see all the hidden devices
            await GetAreasList();
            var response = (await ComelitGetZonesDescription()) ?? new ZoneDescriptionResponseDTO();
            var responseStatus = (await ComelitGetZonesStatus()) ?? new ZoneStatusResponseDTO();

            Zones.Clear();
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

                    Zones.Add(zone);
                }
            }


            if (removeHiddenZones) Zones = Zones.Where(x => !x.Hidden).ToList();

            return Zones.Where(x => idArea == -1 || x.AreaId == idArea).ToList();

        }

        public async Task<bool> ArmAlarm(int? area = null, bool force = true)
        {
            return await ComelitApiPostCall(new Dictionary<string, string>() {
                { "forced", force ? "1" : "0" },
                { "vedo_param", "1" },
                { "type_param", "tot" },
                { "area_param", area?.ToString() ?? "32" } //32 = all the areas
            });
        }

        public async Task<bool> DisarmAlarm(int? area = null, bool force = true)
        {
            return await ComelitApiPostCall(new Dictionary<string, string>() {
                { "forced", force ? "1" : "0" },
                { "vedo_param", "1" },
                { "type_param", "dis" },
                { "area_param", area?.ToString() ?? "32" }
            });
        }

        public async Task<bool> ExcludeZone(int zoneId)
        {
            return await ComelitApiActionCall(new Dictionary<string, string>() {
                { "vedo", "1" },
                { "excl", zoneId.ToString() }
            });
        }

        public async Task<bool> IncludeZone(int zoneId)
        {
            return await ComelitApiActionCall(new Dictionary<string, string>() {
                { "vedo", "1" },
                { "incl", zoneId.ToString() }
            });
        }

        public async Task<bool> IsolateZone(int zoneId)
        {
            return await ComelitApiActionCall(new Dictionary<string, string>() {
                { "vedo", "1" },
                { "isol", zoneId.ToString() }
            });
        }

        public async Task<bool> UnisolateZone(int zoneId)
        {
            return await ComelitApiActionCall(new Dictionary<string, string>() {
                { "vedo", "1" },
                { "activ", zoneId.ToString() }
            });
        }

        #endregion

        #region private

        private async Task<HttpClient> BuildHttpClient(bool isLogin = false)
        {
            HttpClient client = new();

            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_4) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.113 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            client.BaseAddress = new Uri(_BASE_ADDRESS);
            client.Timeout = TimeSpan.FromSeconds(10);

            if (!isLogin)
            {
                //If cookie is empty i call login to get new UID
                if (String.IsNullOrEmpty(_UID))
                {
                    await Login();
                }

                client.DefaultRequestHeaders.Add("Cookie", _UID);
            }



            return client;
        }

        private string SetCookieUID(HttpResponseMessage response)
        {
            try
            {
                //Cookie UID is returned into header reasponse, inside field "Set-Cookie".
                //Example value: uid=XXXXXXXXXXX
                _UID = response.Headers.GetValues("Set-Cookie").First(); //.Split("=").Last();
                return _UID;
            }
            catch
            {
                return "";
            }
        }

        protected string GetCookieUID()
        {
            try
            {
                if (!String.IsNullOrEmpty(_UID)) return _UID;

                return _UID;
            }
            catch
            {
                return "";
            }
        }


        #endregion
    }
}