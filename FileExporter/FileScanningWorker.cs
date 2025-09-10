using FileExporter.Models;
using FileExporter.Services;
using Microsoft.Extensions.Options;

namespace FileExporter
{
    public class FileScanningWorker : BackgroundService
    {
        private readonly ILogger<FileScanningWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Settings _settings;

        public FileScanningWorker(ILogger<FileScanningWorker> logger, IServiceProvider serviceProvider, IOptions<Settings> settings)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _settings = settings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("File Scanning Worker running.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Starting periodic scan cycle at: {time}", DateTimeOffset.Now);

                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var scanManager = scope.ServiceProvider.GetRequiredService<ScanManagerService>();

                        // The worker's only job is to kick off the full discovery and scan process.
                        await scanManager.DiscoverAndScanAllAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An unhandled exception occurred during the periodic scan cycle.");
                }

                _logger.LogInformation("Scan cycle finished. Waiting for {ScanIntervalMinutes} minutes until the next cycle.", _settings.ScanIntervalMinutes);
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_settings.ScanIntervalMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // This is expected on shutdown, no need to log an error.
                    _logger.LogInformation("File Scanning Worker is stopping.");
                }
            }
        }
    }
}
