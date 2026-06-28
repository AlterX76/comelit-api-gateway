using ComelitApiGateway.Commons.Exceptions;
using ComelitApiGateway.Commons.Interfaces;
using ComelitApiGateway.Services;
using System.Net;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

//Get environment variable injected from docker
builder.Configuration.AddEnvironmentVariables();

if (String.IsNullOrEmpty(builder.Configuration["VEDO_KEY"]))
{
    throw new GeneralException("VEDO_KEY is not set");
}

if (String.IsNullOrEmpty(builder.Configuration["VEDO_URL"]))
{
    throw new GeneralException("VEDO_URL is not set");
}

// Configure TimeZone from appsettings
var timeZone = builder.Configuration["TimeZone"];
if (!string.IsNullOrEmpty(timeZone))
{
    Environment.SetEnvironmentVariable("TZ", timeZone);
}

// Add services to the container.
builder.Services.AddSingleton<IComelitVedo, ComelitVedoService>();

// Named HTTP client for the Comelit VEDO panel.
// The session cookie (uid) is handled automatically by the CookieContainer. The handler
// is reused for a full day before being rotated: long enough to avoid pointless churn
// (single fixed internal host), short enough to refresh TCP connections daily. A lost or
// expired session is recovered transparently by the "Not logged" re-login/retry logic in
// ComelitVedoService, so the exact lifetime is not safety-critical.
builder.Services.AddHttpClient("vedo", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["VEDO_URL"]!);
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_4) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.113 Safari/537.36");
    client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    CookieContainer = new CookieContainer(),
    UseCookies = true
})
.SetHandlerLifetime(TimeSpan.FromDays(1));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath, true);

});

// Active ping for keep-alive to avoid session timeout on 
// Comelit CGI and avoid Comelit sleep mode
if (Convert.ToBoolean(builder.Configuration["KEEPALIVE_ENABLED"]))
{
    builder.Services.AddHostedService<ComelitKeepAliveService>();
}

var app = builder.Build();

if (app.Environment.IsDevelopment() || Convert.ToBoolean(builder.Configuration["ENABLE_SWAGGER"]) == true)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


//Disabled because is only in internal network
//app.UseHttpsRedirection();

//app.UseAuthorization();

app.MapControllers();

app.Run();
