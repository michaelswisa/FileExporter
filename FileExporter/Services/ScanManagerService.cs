using FileExporter.Interface;
using FileExporter.Models;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace FileExporter.Services
{
    public class ScanManagerService
    {
        private readonly ILogger<ScanManagerService> _logger;
        private readonly Settings _settings;
        private readonly IFailureSearchService _failureSearcher;
        private readonly IZombieSearchService _zombieSearcher;
        private readonly ITranscodedSearchService _transcodedSearcher;
        private readonly IFileHelper _fileHelper;

        private const string FailedSubDir = "Failed";
        private const string TranscodedSuffix = "-transcoded";

        private static readonly Regex GeneralPattern = new Regex(@"^\w+(-\w+)*-landing-?dir-\w+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly HashSet<string> ValidEnvs = new(StringComparer.OrdinalIgnoreCase) { "dev", "int", "prod" };

        public ScanManagerService(
            ILogger<ScanManagerService> logger,
            IOptions<Settings> settings,
            IFailureSearchService failureSearcher,
            IZombieSearchService zombieSearcher,
            ITranscodedSearchService transcodedSearcher,
            IFileHelper fileHelper)
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

            using var semaphore = new SemaphoreSlim(_settings.MaxParallelDNameScans);

            var subDirNames = await _fileHelper.GetSubDirectories(_settings.RootPath);
            var allScanTasks = new List<Task>();

            foreach (var dirName in subDirNames)
            {
                var parsedInfo = ParseAndValidateDirectoryName(dirName);
                if (parsedInfo == null) continue;

                var (dName, env) = parsedInfo.Value;

                if (!env.Equals(_settings.Env, StringComparison.OrdinalIgnoreCase)) continue;

                await semaphore.WaitAsync();

                _logger.LogInformation("Semaphore acquired for dName: {DName}. Queueing up scans.", dName);

                var task = ScanAllTypesForDNameAsync(dName)
                    .ContinueWith(t =>
                    {
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

        public virtual async Task<ScanAllResult> ScanAllTypesForDNameAsync(string dName)
        {
            _logger.LogInformation("Executing all scan types for dName: {DName}", dName);
            var result = new ScanAllResult();

            // We can run the directory checks in parallel
            var failureTask = ScanFailuresForDNameAsync(dName);
            var observedZombieTask = ScanZombiesForDNameAsync(dName, ZombieType.Observed);
            var nonObservedZombieTask = ScanZombiesForDNameAsync(dName, ZombieType.Non_Observed);
            var transcodedTask = ScanTranscodedForDNameAsync(dName);

            // Wait for all checks to complete
            await Task.WhenAll(failureTask, observedZombieTask, nonObservedZombieTask, transcodedTask);

            // Populate the result object
            result.FailureScanQueued = failureTask.Result;
            result.ObservedZombieScanQueued = observedZombieTask.Result;
            result.NonObservedZombieScanQueued = nonObservedZombieTask.Result;
            result.TranscodedScanQueued = transcodedTask.Result;

            // Add informative messages
            result.Messages.Add($"Failure scan: {(result.FailureScanQueued ? "Queued" : "Skipped (directory not found)")}.");
            result.Messages.Add($"Observed Zombie scan: {(result.ObservedZombieScanQueued ? "Queued" : "Skipped (directory not found)")}.");
            result.Messages.Add($"Non-Observed Zombie scan: {(result.NonObservedZombieScanQueued ? "Queued" : "Skipped (directory not found)")}.");
            result.Messages.Add($"Transcoded scan: {(result.TranscodedScanQueued ? "Queued" : "Skipped (directory not found)")}.");

            return result;
        }

        public virtual async Task<bool> ScanFailuresForDNameAsync(string inputDName)
        {
            var dirInfo = await FindDirectoryInfoAsync(inputDName);
            if (dirInfo == null)
            {
                _logger.LogWarning("Could not find a matching directory for dName '{dName}' in environment '{env}'. Skipping failure scan.", inputDName, _settings.Env);
                return false;
            }

            var (dName, env, actualDirName) = dirInfo.Value;
            var failedDirPath = Path.Combine(_settings.RootPath, FailedSubDir, actualDirName);

            if (!Directory.Exists(failedDirPath))
            {
                _logger.LogWarning("'Failed' directory not found for dName {dName} at {Path}. Skipping failure scan.", dName, failedDirPath);
                return false;
            }

            _logger.LogInformation("Initiating failure scan for dName {dName}", dName);
            _ = _failureSearcher.SearchFolderForFailuresAsync(Path.Combine(_settings.RootPath, FailedSubDir), failedDirPath, dName, env)
                .ContinueWith(t => {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Background failure scan for dName {dName} failed.", dName);
                    }
                });
            return true;
        }

        public virtual async Task<bool> ScanZombiesForDNameAsync(string inputDName, ZombieType zombieType)
        {
            var dirInfo = await FindDirectoryInfoAsync(inputDName);
            if (dirInfo == null)
            {
                _logger.LogWarning("Could not find a matching directory for dName '{dName}' in environment '{env}'. Skipping {zombieType} zombie scan.", inputDName, _settings.Env, zombieType);
                return false;
            }

            var (dName, env, actualDirName) = dirInfo.Value;
            var originalDirPath = Path.Combine(_settings.RootPath, actualDirName);

            _logger.LogInformation("Initiating {zombieType} zombie scan for dName {dName}", zombieType, dName);

            Task scanTask;
            if (zombieType == ZombieType.Observed)
            {
                scanTask = _zombieSearcher.SearchFolderForObservedZombiesAsync(_settings.RootPath, originalDirPath, dName, env);
            }
            else
            {
                scanTask = _zombieSearcher.SearchFolderForNonObservedZombiesAsync(_settings.RootPath, originalDirPath, dName, env);
            }

            _ = scanTask.ContinueWith(t => {
                if (t.IsFaulted)
                {
                    _logger.LogError(t.Exception, "Background {zombieType} zombie scan for dName {dName} failed.", zombieType, dName);
                }
            });

            return true;
        }

        public virtual async Task<bool> ScanTranscodedForDNameAsync(string inputDName)
        {
            // First, find the base directory to get the correct dName casing and env
            var dirInfo = await FindDirectoryInfoAsync(inputDName);
            if (dirInfo == null)
            {
                _logger.LogWarning("Could not find a base directory for dName '{dName}'. Skipping transcoded scan.", inputDName);
                return false;
            }

            var (dName, env, _) = dirInfo.Value;

            var transcodedInfo = await FindTranscodedDirectoryInfoAsync(dName);

            // 2. Check if the result itself is null (meaning no directory was found)
            if (transcodedInfo == null)
            {
                _logger.LogWarning("'transcoded' directory not found for dName {dName}. Skipping transcoded scan.", dName);
                return false;
            }

            // 3. Now that we know transcodedInfo is not null, we can safely access its .Value and deconstruct it.
            var (actualTranscodedDirName, transcodedPath) = transcodedInfo.Value;

            _logger.LogInformation("Initiating transcoded scan for dName {dName}", dName);
            _ = _transcodedSearcher.SearchFoldersForTranscodedAsync(_settings.RootPath, transcodedPath!, dName, env)
                 .ContinueWith(t => {
                     if (t.IsFaulted)
                     {
                         _logger.LogError(t.Exception, "Background transcoded scan for dName {dName} failed.", dName);
                     }
                 });

            return true;
        }

        #endregion

        #region Private Helpers
        private async Task<(string dName, string env, string actualDirName)?> FindDirectoryInfoAsync(string inputDName)
        {
            var subDirNames = await _fileHelper.GetSubDirectories(_settings.RootPath);
            foreach (var dirName in subDirNames)
            {
                var parsedInfo = ParseAndValidateDirectoryName(dirName);
                if (parsedInfo == null) continue;

                var (parsedDName, parsedEnv) = parsedInfo.Value;

                if (parsedEnv.Equals(_settings.Env, StringComparison.OrdinalIgnoreCase) &&
                    parsedDName.Equals(inputDName, StringComparison.OrdinalIgnoreCase))
                {
                    // Return the parsed dName and env, and the *actual* directory name with its correct casing
                    return (parsedDName, parsedEnv, dirName);
                }
            }
            return null;
        }

        private async Task<(string? actualDirName, string? fullPath)?> FindTranscodedDirectoryInfoAsync(string dName)
        {
            var subDirNames = await _fileHelper.GetSubDirectories(_settings.RootPath);
            var expectedDirName = $"{dName}{TranscodedSuffix}";

            foreach (var dirName in subDirNames)
            {
                if (dirName.Equals(expectedDirName, StringComparison.OrdinalIgnoreCase))
                {
                    return (dirName, Path.Combine(_settings.RootPath, dirName));
                }
            }
            return null;
        }

        public (string dName, string env)? ParseAndValidateDirectoryName(string? dirName)
        {
            if (string.IsNullOrEmpty(dirName))
            {
                return null;
            }

            if (!GeneralPattern.IsMatch(dirName))
            {
                _logger.LogDebug("Directory '{DirName}' failed general regex pattern validation. Skipping.", dirName);
                return null;
            }

            var parts = dirName.Split('-');
            if (parts.Length < 2) return null;

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
