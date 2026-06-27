using ComelitApiGateway.Commons.Exceptions;
using ComelitApiGateway.Commons.Interfaces;
using ComelitApiGateway.Services;
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

#if DEBUG
    builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(5000));
#endif

// Add services to the container.
builder.Services.AddSingleton<IComelitVedo, ComelitVedoService>();

builder.Services.AddHttpClient();
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
