using ComelitApiGateway.Commons.Dtos.Vedo;
using ComelitApiGateway.Commons.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ComelitApiGateway.Services
{
    public class ComelitKeepAliveService(
        IComelitVedo comelitClient,
        ILogger<ComelitKeepAliveService> logger) : BackgroundService
    {
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

        private readonly IComelitVedo comelitVedoClient = comelitClient;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("ComelitKeepAliveService started.");

            using PeriodicTimer timer = new(_interval);

            // The loop advances only when the timer ticks (every 1 minute)
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await PingStatusAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The 'when' block prevents logging errors if the application is simply shutting down
                    logger.LogError(ex, "Error during keep-alive ping.");
                }
            }
        }

        private async Task PingStatusAsync(CancellationToken cancellationToken)
        {
            // Use GetAsync passing the cancellation token directly
            var response = await comelitVedoClient.GetAreasStatus();

            if (response.Any())
            {
                logger.LogInformation("Ping was successful.");
            }
            else
            {
                _ = await comelitVedoClient.Login();
                logger.LogWarning("Ping failed, re-login attempted.");
            }
        }
    }
}