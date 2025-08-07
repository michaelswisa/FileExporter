using System.Text.RegularExpressions;
using FileExporterNew.Models;
using FileExporterNew.Services;
using Microsoft.Extensions.Options;

namespace FileExporterGinari
{
    public class ScanManagerService
    {
        private readonly ILogger<ScanManagerService> _logger;
        private readonly Settings _settings;
        private readonly FailureSearchService _failureSearcher;
        private readonly ZombieSearchService _zombieSearcher;
        private readonly TranscodedSearchService _transcodedSearcher;
        private readonly FileHelper _fileHelper;

        private const string FailedSubDir = "Failed";
        private const string TranscodedSuffix = "-transcoded";

        private static readonly Regex GeneralPattern = new Regex(@"^\w+(-\w+)*-landing-?dir-\w+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly HashSet<string> ValidEnvs = new(StringComparer.OrdinalIgnoreCase) { "dev", "int", "prod" };

        public ScanManagerService(
            ILogger<ScanManagerService> logger,
            IOptions<Settings> settings,
            FailureSearchService failureSearcher,
            ZombieSearchService zombieSearcher,
            TranscodedSearchService transcodedSearcher,
            FileHelper fileHelper)
        {
            _logger = logger;
            _settings = settings.Value;
            _failureSearcher = failureSearcher;
            _zombieSearcher = zombieSearcher;
            _transcodedSearcher = transcodedSearcher;
            _fileHelper = fileHelper;
        }

        #region Periodic Scan (Worker)

        public async Task DiscoverAndScanAllAsync()
        {
            _logger.LogInformation(
                "Starting new controlled parallel discovery and scan flow. Max parallel scans: {MaxParallel}",
                _settings.MaxParallelDNameScans);

            // הגבלת המקביליות על בסיס ההגדרה
            using var semaphore = new SemaphoreSlim(_settings.MaxParallelDNameScans);

            var subDirNames = await _fileHelper.GetSubDirectories(_settings.RootPath);
            var allScanTasks = new List<Task>();

            foreach (var dirName in subDirNames)
            {
                var parsedInfo = ParseAndValidateDirectoryName(dirName);
                if (parsedInfo == null) continue;

                var (dName, env) = parsedInfo.Value;

                if (!env.Equals(_settings.Env, StringComparison.OrdinalIgnoreCase)) continue;

                // המתן עד שיהיה "סלוט" פנוי בסמפור
                await semaphore.WaitAsync();

                _logger.LogInformation("Semaphore acquired for dName: {DName}. Queueing up scans.", dName);

                // יצירת המשימה והוספת שחרור הסמפור בסיומה
                var task = ScanAllTypesForDNameAsync(dName)
                    .ContinueWith(t =>
                    {
                        // שחרור הסמפור יתבצע תמיד, גם אם המשימה נכשלה
                        semaphore.Release();
                        _logger.LogInformation("Semaphore released for dName: {DName}", dName);
                        if (t.IsFaulted)
                        {
                            _logger.LogError(t.Exception, "A scan task for dName {DName} failed.", dName);
                        }
                    });

                allScanTasks.Add(task);
            }

            if (allScanTasks.Any())
            {
                _logger.LogInformation("Waiting for {TaskCount} dName scan groups to complete.", allScanTasks.Count);
                await Task.WhenAll(allScanTasks);
                _logger.LogInformation("All controlled parallel dName scans have completed.");
            }
            else
            {
                _logger.LogInformation("No valid dName directories found to scan in the configured environment.");
            }
        }

        #endregion

        #region On-Demand Scans (API Controller)

        public async Task<bool> ScanAllTypesForDNameAsync(string dName)
        {
            _logger.LogInformation("Executing all scan types for dName: {DName}", dName);

            var failureTask = ScanFailuresForDNameAsync(dName);
            var observedZombieTask = ScanZombiesForDNameAsync(dName, "observed");
            var nonObservedZombieTask = ScanZombiesForDNameAsync(dName, "non-observed");
            var transcodedTask = ScanTranscodedForDNameAsync(dName);

            await Task.WhenAll(failureTask, observedZombieTask, nonObservedZombieTask, transcodedTask);

            // Return true if at least one scan was potentially successful.
            // The individual methods log if a directory was not found.
            return true;
        }

        public async Task<bool> ScanFailuresForDNameAsync(string dName)
        {
            var env = _settings.Env;
            var dirName = BuildDirectoryName(dName, env);
            var failedDirPath = Path.Combine(_settings.RootPath, FailedSubDir, dirName);

            if (!Directory.Exists(failedDirPath))
            {
                _logger.LogWarning("'Failed' directory not found for dName {dName} at {Path}. Skipping failure scan.", dName, failedDirPath);
                return false;
            }

            _logger.LogInformation("Initiating failure scan for dName {dName}", dName);
            await _failureSearcher.SearchFolderForFailuresAsync(Path.Combine(_settings.RootPath, FailedSubDir), failedDirPath, dName, env);
            return true;
        }

        public async Task<bool> ScanZombiesForDNameAsync(string dName, string zombieType)
        {
            var env = _settings.Env;
            var dirName = BuildDirectoryName(dName, env);
            var originalDirPath = Path.Combine(_settings.RootPath, dirName);

            if (!Directory.Exists(originalDirPath))
            {
                _logger.LogWarning("Original directory not found for dName {dName} at {Path}. Skipping {zombieType} zombie scan.", dName, originalDirPath, zombieType);
                return false;
            }

            _logger.LogInformation("Initiating {zombieType} zombie scan for dName {dName}", zombieType, dName);
            if (zombieType == "observed")
            {
                await _zombieSearcher.SearchFolderForObservedZombiesAsync(_settings.RootPath, originalDirPath, dName, env);
            }
            else
            {
                await _zombieSearcher.SearchFolderForNonObservedZombiesAsync(_settings.RootPath, originalDirPath, dName, env);
            }
            return true;
        }

        public async Task<bool> ScanTranscodedForDNameAsync(string dName)
        {
            var env = _settings.Env;
            var transcodedDirName = $"{dName}{TranscodedSuffix}"; // Assuming this is how it's built
            var transcodedDirPath = Path.Combine(_settings.RootPath, transcodedDirName);

            if (!Directory.Exists(transcodedDirPath))
            {
                _logger.LogWarning("'transcoded' directory not found for dName {dName} at {Path}. Skipping transcoded scan.", dName, transcodedDirPath);
                return false;
            }

            _logger.LogInformation("Initiating transcoded scan for dName {dName}", dName);
            await _transcodedSearcher.SearchFoldersForTranscodedAsync(_settings.RootPath, transcodedDirPath, dName, env);
            return true;
        }

        #endregion

        #region Private Helpers

        private string BuildDirectoryName(string dName, string env)
        {
            // This is an assumption based on your regex. You might need to adjust this.
            // Example: "myservice-landing-dir-prod"
            return $"{dName}-landing-dir-{env}";
        }

        private (string dName, string env)? ParseAndValidateDirectoryName(string dirName)
        {
            if (!GeneralPattern.IsMatch(dirName))
            {
                _logger.LogDebug("Directory '{DirName}' failed general regex pattern validation. Skipping.", dirName);
                return null;
            }

            var parts = dirName.Split('-');
            if (parts.Length < 2) return null;

            // Assuming dName is everything before "-landing-dir"
            var landingIndex = Array.IndexOf(parts, "landing");
            if (landingIndex <= 0) return null;

            var dName = string.Join("-", parts.Take(landingIndex));
            var env = parts[^1];

            if (!ValidEnvs.Contains(env)) return null;

            return (dName, env);
        }
        #endregion
    }
}
