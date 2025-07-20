using FileExporterNew.Models;
using FileExporterNew.Services;
using Microsoft.Extensions.Options;

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

        // לולאה שתרוץ כל עוד האפליקציה לא קיבלה הוראת כיבוי
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Starting periodic scan at: {time}", DateTimeOffset.Now);

            try
            {
                // שימוש ב-CreateScope כדי לקבל מופעים חדשים של השירותים בכל ריצה.
                // זה מונע בעיות של אורך חיים (lifetime) של אובייקטים, במיוחד אם יש תלויות ב-DBContext וכדומה.
                using (var scope = _serviceProvider.CreateScope())
                {
                    var failureSearcher = scope.ServiceProvider.GetRequiredService<FailureSearchService>();
                    var zombieSearcher = scope.ServiceProvider.GetRequiredService<ZombieSearchService>();
                    var transcodedSearcher = scope.ServiceProvider.GetRequiredService<TranscodedSearchService>();

                    // רשימת התיקיות לסריקה (d_names)
                    // ניתן לקרוא אותן ישירות מתוך RootPath או להגדיר אותן בקונפיגורציה
                    var dNamesToScan = Directory.GetDirectories(_settings.RootPath).Select(Path.GetFileName).ToList();

                    var tasks = new List<Task>();

                    foreach (var dName in dNamesToScan)
                    {
                        var dNamePath = Path.Combine(_settings.RootPath, dName);

                        // הוספת כל הסריקות לרשימת משימות שתרוץ במקביל
                        tasks.Add(failureSearcher.SearchFolderForFailuresAsync(_settings.RootPath, dNamePath, dName, _settings.Env));
                        tasks.Add(zombieSearcher.SearchFolderForObservedZombiesAsync(_settings.RootPath, dNamePath, dName, _settings.Env));
                        tasks.Add(zombieSearcher.SearchFolderForNonObservedZombiesAsync(_settings.RootPath, dNamePath, dName, _settings.Env));
                        tasks.Add(transcodedSearcher.SearchFoldersForTranscodedAsync(_settings.RootPath, dNamePath, dName, _settings.Env));
                    }

                    // המתנה לסיום כל המשימות
                    await Task.WhenAll(tasks);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred during the scan cycle.");
            }

            // המתנה של 5 דקות עד לסריקה הבאה
            _logger.LogInformation($"Scan cycle finished. Waiting for {_settings.ScanIntervalMinutes} minutes until the next cycle.");

            await Task.Delay(TimeSpan.FromMinutes(_settings.ScanIntervalMinutes), stoppingToken);
        }
    }
}