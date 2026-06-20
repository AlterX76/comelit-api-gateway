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
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // This is a normal shutdown, just exit the loop
                    break;
                }
                catch (Exception ex)
                {
                    // Log the error so you know the API failed, 
                    // but DO NOT let it escape the method.
                    _logger.LogError(ex, "Error occurred during Comelit keep-alive ping.");
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