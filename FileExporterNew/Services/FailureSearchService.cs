using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using FileExporterNew.Models;

namespace FileExporterNew.Services
{
    public class FailureSearchService : IDisposable
    {
        private readonly Settings _settings;
        private readonly ILogger<FailureSearchService> _logger;
        private readonly SemaphoreSlim _semaphore;
        private readonly MetricsManager _metricsManager;
        private readonly FileHelper _fileHelper;
        private static readonly ConcurrentDictionary<string, (HashSet<string> keys, DateTime lastUsed)> _activeGroupFolderSeries = new();

        public FailureSearchService(
            IOptions<Settings> settings,
            ILogger<FailureSearchService> logger,
            MetricsManager metricsManager,
            FileHelper fileHelper)
        {
            _settings = settings.Value;
            _logger = logger;
            _metricsManager = metricsManager;
            _fileHelper = fileHelper;
            _semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
        }

        // 2. הפונקציה הראשית — שימו לב לשינוי ב-finally
        public async Task SearchFolderForFailuresAsync(
            string rootDir,
            string path,
            string dName,
            string env)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogDebug("SearchFolderForFailures on path: {Path}", path);

            try
            {
                var normalizedDName = char.ToUpper(dName[0]) + dName[1..];

                var allFailureReasons = new List<FailureReason>();
                var directoryMetrics = new Dictionary<string, (int failureCount, DateTime lastModified)>();

                await ScanDirectoryTreeAsync(
                    rootDir, path, env, normalizedDName,
                    allFailureReasons, directoryMetrics);

                var recentWindow = TimeSpan.FromMinutes(_settings.RecentErrorsTimeWindowMinutes);
                var now = DateTime.Now;
                var recentFailures = allFailureReasons
                    .Where(fr => (now - fr.LastWriteTime) <= recentWindow)
                    .ToList();

                await LogAllMetricsAsync(
                    allFailureReasons, recentFailures, directoryMetrics,
                    rootDir, path, normalizedDName, env);

                await SaveFailureReasonsToJsonAsync(allFailureReasons, path, "reasons_all.json");
                await SaveFailureReasonsToJsonAsync(recentFailures, path, "reasons_recent.json");

                _logger.LogInformation(
                    "Completed search for {DName}: {TotalFailures} total failures, {RecentFailures} recent failures",
                    normalizedDName, allFailureReasons.Count, recentFailures.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SearchFolderForFailures");
                throw;
            }
            finally
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "Scan completed for {DName} in {Elapsed} ms",
                    dName, stopwatch.ElapsedMilliseconds);

                // קריאה לפונקציה הייעודית
                RecordScanDurationMetric(
                    rootDir, dName, env, stopwatch.ElapsedMilliseconds);
            }
        }

        // 1. פונקציית עזר אחראית רק על כתיבת המטריקה
        private void RecordScanDurationMetric(
            string rootDir,
            string dName,
            string env,
            long elapsedMilliseconds)
        {
            _metricsManager.SetGaugeValue(
                "dname_scan_duration_ms",
                "Duration of d_name scan in milliseconds",
                new[] { "root_dir", "d_name", "env" },
                new[] { rootDir, dName, env },
                elapsedMilliseconds);
        }


        private async Task ScanDirectoryTreeAsync(
            string rootDir,
            string currentPath,
            string env,
            string dName,
            List<FailureReason> allFailureReasons,
            Dictionary<string, (int failureCount, DateTime lastModified)> directoryMetrics)
        {
            var directoriesToProcess = new Queue<(string path, int depth)>();
            directoriesToProcess.Enqueue((currentPath, 0));

            while (directoriesToProcess.Count > 0)
            {
                var (dirPath, depth) = directoriesToProcess.Dequeue();

                try
                {
                    // Process current directory
                    var (failures, reasons, lastModified) = await ProcessDirectoryAsync(dirPath);
                    
                    allFailureReasons.AddRange(reasons);

                    // Store directory metrics
                    var dirName = new DirectoryInfo(dirPath).Name;
                    directoryMetrics[dirPath] = (failures, lastModified);

                    // Determine if we should recurse further
                    if (ShouldRecurseIntoSubdirs(dName, depth))
                    {
                        var subdirs = await _fileHelper.GetSubDirectories(dirPath);
                        foreach (var subdir in subdirs)
                        {
                            var fullSubdirPath = Path.Combine(dirPath, subdir);
                            directoriesToProcess.Enqueue((fullSubdirPath, depth + 1));
                        }
                    }

                    // Break if we hit max failures
                    if (allFailureReasons.Count >= _settings.MaxFailures)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing directory {DirPath}", dirPath);
                }
            }
        }

        private async Task<(int failures, List<FailureReason> reasons, DateTime lastModified)> ProcessDirectoryAsync(
            string dirPath)
        {
            var lastModified = DateTime.MinValue;
            var reasons = new List<FailureReason>();
            int nFailures = 0;

            try
            {
                (nFailures, reasons) = await _fileHelper.NumberOfFaileds(dirPath);
                lastModified = GetLastModificationTime(dirPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing directory {DirPath}", dirPath);
            }

            return (nFailures, reasons, lastModified);
        }

        private DateTime GetLastModificationTime(string path)
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);
                return dirInfo.Exists ? dirInfo.LastWriteTime : DateTime.MinValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting modification time for {Path}", path);
                return DateTime.MinValue;
            }
        }

        private bool ShouldRecurseIntoSubdirs(string dName, int depth)
        {
            if (_settings.GroupedDNnames.Contains(dName.ToLower()))
            {
                return depth < _settings.MaxDepth;
            }
            else
            {
                return depth < 1;
            }
        }

        private Task LogAllMetricsAsync(
            List<FailureReason> allFailures,
            List<FailureReason> recentFailures,
            Dictionary<string, (int failureCount, DateTime lastModified)> directoryMetrics,
            string rootDir,
            string path,
            string dName,
            string env)
        {
            // Only log group folder metrics for grouped DN names
            if (_settings.GroupedDNnames.Contains(dName.ToLower()))
            {
                LogGroupFolderMetrics(allFailures, rootDir, path, dName, env, false);
                LogGroupFolderMetrics(recentFailures, rootDir, path, dName, env, true);
            }

            // Log global metrics
            LogGlobalMetrics(allFailures.Count, rootDir, dName, env, false);
            LogGlobalMetrics(recentFailures.Count, rootDir, dName, env, true);
            return Task.CompletedTask;
        }

        private void LogGroupFolderMetrics(
            List<FailureReason> failureReasons,
            string rootDir,
            string path,
            string dName,
            string env,
            bool isRecent)
        {
            var folderFailureCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var metricName = "n_failures_in_group_folder"; // Define metricName here
            var currentKeys = new HashSet<string>(StringComparer.Ordinal); // Initialize currentKeys

            // 1. Get all "true" group folders (folders that contain other directories) and initialize their counts to 0.
            var allGroupFolders = GetAllGroupFolders(path, dName);
            foreach (var folder in allGroupFolders)
            {
                folderFailureCounts[folder] = 0;
            }

            // 2. Iterate through each failure and increment the count for its parent group folders.
            foreach (var failure in failureReasons)
            {
                var currentDir = Directory.GetParent(failure.Path);
                // Traverse up from the failure's directory.
                while (currentDir != null && currentDir.FullName.Length >= path.Length && currentDir.FullName.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                {
                    // Only increment the count if the directory is a pre-identified group folder.
                    if (folderFailureCounts.ContainsKey(currentDir.FullName))
                    {
                        folderFailureCounts[currentDir.FullName]++;
                    }
                    currentDir = currentDir.Parent;
                }
            }

            // 3. Report the final count for each folder, skipping the top-level dName folder.
            foreach (var kvp in folderFailureCounts)
            {
                var folderPath = kvp.Key;
                var failureCount = kvp.Value;
                var folderName = new DirectoryInfo(folderPath).Name;

                // Skip reporting the metric for the root search path itself, as it's covered by total_nFailures.
                if (folderPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _metricsManager.SetGaugeValue(
                    "n_failures_in_group_folder",
                    "failures in grouped subfolder",
                    new[] { "root_dir", "d_name", "env", "group_folder", "is_recent" },
                    new[] { rootDir, dName, env, folderName, isRecent ? "true" : "false" },
                    failureCount);

                // Add key to currentKeys for series tracking
                var labelValuesForCurrentKey = new[] { rootDir, dName, env, folderName, isRecent ? "true" : "false" };
                var key = BuildKey(labelValuesForCurrentKey);
                currentKeys.Add(key);
            }

            // Remove stale series for this dName + isRecent combination
            var dNameAndRecentKey = $"{dName}_{isRecent}"; // Unique key for this specific d_name and is_recent combination
            
            _logger.LogInformation($"Processing cleanup for {dNameAndRecentKey}. Current folders: {currentKeys.Count}");
            
            if (_activeGroupFolderSeries.TryGetValue(dNameAndRecentKey, out var previousData))
            {
                _logger.LogInformation($"Previous folders: {previousData.keys.Count}");
                var staleKeys = previousData.keys.Except(currentKeys).ToList();
                _logger.LogInformation($"Stale folders to remove: {staleKeys.Count}");
                
                foreach (var stale in staleKeys)
                {
                    var labelValues = SplitKey(stale);
                    var folderName = labelValues[3]; // group_folder is at index 3
                    
                    // First set to 0, then remove - this ensures Prometheus clears the metric
                    _metricsManager.SetGaugeValue(metricName, "failures in grouped subfolder", 
                        new[] { "root_dir", "d_name", "env", "group_folder", "is_recent" }, 
                        labelValues, 0);
                    
                    // Then remove the series
                    _metricsManager.RemoveGaugeSeries(metricName, labelValues);
                    _logger.LogWarning($"REMOVED stale metric for folder: {folderName} - Key: {stale}");
                }
            }
            else
            {
                _logger.LogInformation($"No previous keys found for {dNameAndRecentKey}");
            }
            
            // Update with current keys and timestamp
            _activeGroupFolderSeries[dNameAndRecentKey] = (currentKeys, DateTime.Now);
            
            // Clean up old entries (older than 24 hours)
            CleanupOldEntries();
        }

        private HashSet<string> GetAllGroupFolders(string searchPath, string dName)
        {
            var groupFolders = new HashSet<string>();
            try
            {
                // Add the root search path itself if it's a group folder
                if (Directory.EnumerateDirectories(searchPath).Any())
                {
                    groupFolders.Add(searchPath);
                }

                // Use streaming approach to avoid loading all directories into memory
                var directories = Directory.EnumerateDirectories(searchPath, "*", SearchOption.AllDirectories)
                    .Take(_settings.MaxFailures); // Limit to prevent memory issues
                
                foreach (var dir in directories)
                {
                    if (Directory.EnumerateDirectories(dir).Any())
                    {
                        groupFolders.Add(dir);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting group folders");
            }

            return groupFolders;
        }

        private void LogGlobalMetrics(int totalFailures, string rootDir, string dName, string env, bool isRecent)
        {
            var metricDescription = isRecent ? "recent failures (last 24h)" : "failures";

            _metricsManager.SetGaugeValue(
                "total_nFailures",
                $"total {metricDescription} for d_name",
                new[] { "root_dir", "d_name", "env", "is_recent" },
                new[] { rootDir, dName, env, isRecent ? "true" : "false" },
                totalFailures);
        }

        private async Task SaveFailureReasonsToJsonAsync(
            List<FailureReason> failureReasons,
            string outputPath,
            string fileName)
        {
            try
            {
                var structuredReasons = new Dictionary<string, object>();

                foreach (var fr in failureReasons)
                {
                    var imageFile = fr.Image;
                    if (string.IsNullOrEmpty(imageFile))
                    {
                        imageFile = FindFirstImageInDirAsync(fr.Path);
                    }
                    
                    structuredReasons[fr.Path] = new
                    {
                        reason = fr.Reason,
                        image = imageFile,
                        lastWriteTime = fr.LastWriteTime.ToString("yyyy-MM-ddTHH:mm:ss")
                    };
                }

                var jsonPath = Path.Combine(outputPath, fileName);
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var jsonString = JsonSerializer.Serialize(structuredReasons, jsonOptions);
                await File.WriteAllTextAsync(jsonPath, jsonString);

                _logger.LogInformation("Saved structured failure reasons to {JsonPath}", jsonPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving failure reasons to JSON");
            }
        }

        private string FindFirstImageInDirAsync(string path)
        {
            return "test/test";
        }

        // בניית key ייחודי לפי ערכי labels
        private static string BuildKey(string[] labels) =>
            string.Join('\u0001', labels);

        private static string[] SplitKey(string key) =>
            key.Split('\u0001');

        private void CleanupOldEntries()
        {
            var cutoffTime = DateTime.Now.AddHours(-24);
            var keysToRemove = new List<string>();

            foreach (var kvp in _activeGroupFolderSeries)
            {
                if (kvp.Value.lastUsed < cutoffTime)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (_activeGroupFolderSeries.TryRemove(key, out _))
                {
                    _logger.LogInformation($"Cleaned up old entry: {key}");
                }
            }
        }

        public void Dispose()
        {
            _semaphore?.Dispose();
            // Don't clear _activeGroupFolderSeries - we need it to track stale metrics between requests
        }
    }
}
