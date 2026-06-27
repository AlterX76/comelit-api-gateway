using ComelitApiGateway.Commons.Dtos.Vedo;
using ComelitApiGateway.Commons.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ComelitApiGateway.Services
{
    public class ComelitKeepAliveService(
        IComelitVedo comelitClient,
        IConfiguration configuration,
        ILogger<ComelitKeepAliveService> logger) : BackgroundService
    {
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(
            int.TryParse(configuration["KEEPALIVE_INTERVAL_SECONDS"], out var s) && s > 0 ? s : 60);


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("ComelitKeepAliveService started. Ping interval: {Interval}.", _interval);

            using PeriodicTimer timer = new(_interval);

            // The loop advances only when the timer ticks (every x seconds)
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await PingStatusAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // This is a normal shutdown, just exit the loop
                    break;
                }
                catch (Exception ex)
                {
                    // Log the error so you know the API failed, 
                    // but DO NOT let it escape the method.
                    logger.LogError(ex, "Error occurred during Comelit keep-alive ping.");
                }
            }
        }

        private async Task PingStatusAsync(CancellationToken cancellationToken)
        {
            // Get areas to check if the session is still valid. If not, re-login.
            var response = await comelitClient.GetAreasStatus();

            if (response.Any())
            {
                logger.LogDebug("Ping was successful.");
            }
            else
            {
                _ = await comelitClient.Login();
                logger.LogWarning("Ping failed, re-login attempted.");
            }
        }
    }
}