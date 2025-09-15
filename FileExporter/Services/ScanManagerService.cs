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

        // ללא שינוי
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

        #region Periodic Scan (Worker) - מתודות שממתינות לסיום

        // ללא שינוי
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

                // קוראת לגרסה שממתינה לסיום כל הסריקות
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

        /// <summary>
        /// מריץ את כל סוגי הסריקות עבור dName נתון וממתין לסיומן.
        /// </summary>
        // עודכן - המתודה קוראת כעת לגרסאות ה-Scan...Async שמיועדות ל-Worker
        public virtual async Task<ScanAllResult> ScanAllTypesForDNameAsync(string dName)
        {
            _logger.LogInformation("Executing all scan types for dName: {DName}", dName);
            var result = new ScanAllResult();

            var failureTask = ScanFailuresForDNameAsync(dName);
            var observedZombieTask = ScanZombiesForDNameAsync(dName, ZombieType.Observed);
            var nonObservedZombieTask = ScanZombiesForDNameAsync(dName, ZombieType.Non_Observed);
            var transcodedTask = ScanTranscodedForDNameAsync(dName);

            await Task.WhenAll(failureTask, observedZombieTask, nonObservedZombieTask, transcodedTask);

            result.FailureScanQueued = failureTask.Result;
            result.ObservedZombieScanQueued = observedZombieTask.Result;
            result.NonObservedZombieScanQueued = nonObservedZombieTask.Result;
            result.TranscodedScanQueued = transcodedTask.Result;

            result.Messages.Add($"Failure scan: {(result.FailureScanQueued ? "Completed" : "Skipped")}.");
            result.Messages.Add($"Observed Zombie scan: {(result.ObservedZombieScanQueued ? "Completed" : "Skipped")}.");
            result.Messages.Add($"Non-Observed Zombie scan: {(result.NonObservedZombieScanQueued ? "Completed" : "Skipped")}.");
            result.Messages.Add($"Transcoded scan: {(result.TranscodedScanQueued ? "Completed" : "Skipped")}.");

            return result;
        }

        /// <summary>
        /// מפעיל סריקת כשלים וממתין לסיומה. (עבור ה-Worker)
        /// </summary>
        // עודכן - מימוש חדש שמבוסס על מתודת עזר פרטית
        public virtual async Task<bool> ScanFailuresForDNameAsync(string inputDName)
        {
            var validationResult = await FindAndValidateFailureDirectoryAsync(inputDName);
            if (validationResult == null) return false;

            var (dName, env, failedDirPath) = validationResult.Value;

            try
            {
                _logger.LogInformation("Worker is executing and awaiting failure scan for {dName}", dName);
                await _failureSearcher.SearchFolderForFailuresAsync(Path.Combine(_settings.RootPath, FailedSubDir), failedDirPath, dName, env);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Awaited failure scan for dName {dName} failed.", dName);
                return false;
            }
        }

        /// <summary>
        /// מפעיל סריקת זומבים וממתין לסיומה. (עבור ה-Worker)
        /// </summary>
        // עודכן - מימוש חדש שמבוסס על מתודת עזר פרטית
        public virtual async Task<bool> ScanZombiesForDNameAsync(string inputDName, ZombieType zombieType)
        {
            var validationResult = await FindAndValidateBaseDirectoryAsync(inputDName);
            if (validationResult == null) return false;

            var (dName, env, originalDirPath) = validationResult.Value;

            try
            {
                _logger.LogInformation("Worker is executing and awaiting {zombieType} zombie scan for {dName}", zombieType, dName);
                Task scanTask = zombieType == ZombieType.Observed
                    ? _zombieSearcher.SearchFolderForObservedZombiesAsync(_settings.RootPath, originalDirPath, dName, env)
                    : _zombieSearcher.SearchFolderForNonObservedZombiesAsync(_settings.RootPath, originalDirPath, dName, env);

                await scanTask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Awaited {zombieType} zombie scan for dName {dName} failed.", zombieType, dName);
                return false;
            }
        }

        /// <summary>
        /// מפעיל סריקת קבצים מקודדים מחדש וממתין לסיומה. (עבור ה-Worker)
        /// </summary>
        // עודכן - מימוש חדש שמבוסס על מתודת עזר פרטית
        public virtual async Task<bool> ScanTranscodedForDNameAsync(string inputDName)
        {
            var validationResult = await FindAndValidateTranscodedDirectoryAsync(inputDName);
            if (validationResult == null) return false;

            var (dName, env, transcodedPath) = validationResult.Value;

            try
            {
                _logger.LogInformation("Worker is executing and awaiting transcoded scan for {dName}", dName);
                await _transcodedSearcher.SearchFoldersForTranscodedAsync(_settings.RootPath, transcodedPath, dName, env);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Awaited transcoded scan for dName {dName} failed.", dName);
                return false;
            }
        }
        #endregion

        #region On-Demand Scans (API Controller) - מתודות שמפעילות ברקע

        /// <summary>
        /// מכניס סריקת כשלים לתור לביצוע ברקע. (עבור ה-API)
        /// </summary>
        // חדש - מתודה ייעודית וברורה עבור ה-API
        public virtual async Task<bool> QueueFailureScanForDNameAsync(string inputDName)
        {
            var validationResult = await FindAndValidateFailureDirectoryAsync(inputDName);
            if (validationResult == null) return false;

            var (dName, env, failedDirPath) = validationResult.Value;

            _logger.LogInformation("API is queueing a background failure scan for dName {dName}", dName);
            _ = _failureSearcher.SearchFolderForFailuresAsync(Path.Combine(_settings.RootPath, FailedSubDir), failedDirPath, dName, env)
                .ContinueWith(t => {
                    if (t.IsFaulted) _logger.LogError(t.Exception, "Background failure scan for dName {dName} failed.", dName);
                });

            return true;
        }

        /// <summary>
        /// מכניס סריקת זומבים לתור לביצוע ברקע. (עבור ה-API)
        /// </summary>
        // חדש - מתודה ייעודית וברורה עבור ה-API
        public virtual async Task<bool> QueueZombiesForDNameAsync(string inputDName, ZombieType zombieType)
        {
            var validationResult = await FindAndValidateBaseDirectoryAsync(inputDName);
            if (validationResult == null) return false;

            var (dName, env, originalDirPath) = validationResult.Value;

            _logger.LogInformation("API is queueing a background {zombieType} zombie scan for {dName}", zombieType, dName);

            Task scanTask = zombieType == ZombieType.Observed
                ? _zombieSearcher.SearchFolderForObservedZombiesAsync(_settings.RootPath, originalDirPath, dName, env)
                : _zombieSearcher.SearchFolderForNonObservedZombiesAsync(_settings.RootPath, originalDirPath, dName, env);

            _ = scanTask.ContinueWith(t => {
                if (t.IsFaulted) _logger.LogError(t.Exception, "Background {zombieType} zombie scan for dName {dName} failed.", zombieType, dName);
            });

            return true;
        }

        /// <summary>
        /// מכניס סריקת קבצים מקודדים מחדש לתור לביצוע ברקע. (עבור ה-API)
        /// </summary>
        // חדש - מתודה ייעודית וברורה עבור ה-API
        public virtual async Task<bool> QueueTranscodedScanForDNameAsync(string inputDName)
        {
            var validationResult = await FindAndValidateTranscodedDirectoryAsync(inputDName);
            if (validationResult == null) return false;

            var (dName, env, transcodedPath) = validationResult.Value;

            _logger.LogInformation("API is queueing a background transcoded scan for {dName}", dName);
            _ = _transcodedSearcher.SearchFoldersForTranscodedAsync(_settings.RootPath, transcodedPath, dName, env)
                 .ContinueWith(t => {
                     if (t.IsFaulted) _logger.LogError(t.Exception, "Background transcoded scan for dName {dName} failed.", dName);
                 });

            return true;
        }
        #endregion

        #region Private Helpers - מתודות עזר פרטיות למניעת שכפול קוד

        /// <summary>
        /// מוצא את תיקיית הבסיס, מאמת אותה ומחזיר את המידע הדרוש.
        /// </summary>
        // עודכן - מיקוד מחדש של המתודה הקודמת FindDirectoryInfoAsync
        private async Task<(string dName, string env, string fullPath)?> FindAndValidateBaseDirectoryAsync(string inputDName)
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
                    return (parsedDName, parsedEnv, Path.Combine(_settings.RootPath, dirName));
                }
            }
            _logger.LogWarning("Could not find a base directory for dName '{dName}' in environment '{env}'.", inputDName, _settings.Env);
            return null;
        }

        /// <summary>
        /// מוצא את תיקיית הכשלים, מאמת אותה ומחזיר את המידע הדרוש.
        /// </summary>
        // חדש - מתודת עזר ייעודית
        private async Task<(string dName, string env, string fullPath)?> FindAndValidateFailureDirectoryAsync(string inputDName)
        {
            var baseDirInfo = await FindAndValidateBaseDirectoryAsync(inputDName);
            if (baseDirInfo == null) return null;

            var (dName, env, _) = baseDirInfo.Value;
            // שם התיקייה בפועל יכול להיות עם אותיות גדולות/קטנות שונות, לכן נשתמש בשם שחזר מ-FindAndValidateBaseDirectoryAsync
            var actualDirName = Path.GetFileName(baseDirInfo.Value.fullPath);
            var failedDirPath = Path.Combine(_settings.RootPath, FailedSubDir, actualDirName);

            if (!Directory.Exists(failedDirPath))
            {
                _logger.LogWarning("'Failed' directory not found for dName {dName} at {Path}.", dName, failedDirPath);
                return null;
            }

            return (dName, env, failedDirPath);
        }

        /// <summary>
        /// מוצא את תיקיית ה-transcoded, מאמת אותה ומחזיר את המידע הדרוש.
        /// </summary>
        // עודכן - מיקוד מחדש של המתודה הקודמת FindTranscodedDirectoryInfoAsync
        private async Task<(string dName, string env, string fullPath)?> FindAndValidateTranscodedDirectoryAsync(string inputDName)
        {
            var baseDirInfo = await FindAndValidateBaseDirectoryAsync(inputDName);
            if (baseDirInfo == null) return null;

            var (dName, env, _) = baseDirInfo.Value;
            var expectedDirName = $"{dName}{TranscodedSuffix}";

            var subDirNames = await _fileHelper.GetSubDirectories(_settings.RootPath);
            foreach (var dirName in subDirNames)
            {
                if (dirName.Equals(expectedDirName, StringComparison.OrdinalIgnoreCase))
                {
                    return (dName, env, Path.Combine(_settings.RootPath, dirName));
                }
            }

            _logger.LogWarning("'transcoded' directory not found for dName {dName}.", dName);
            return null;
        }

        // ללא שינוי
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