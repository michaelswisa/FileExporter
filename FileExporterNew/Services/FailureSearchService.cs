using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FileExporterNew.Models;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Services
{
    public class FailureSearchService
    {
        private readonly Settings _settings;
        private readonly ILogger<FailureSearchService> _logger;
        private readonly MetricsManager _metricsManager;
        private readonly FileHelper _fileHelper;
        private static readonly ConcurrentDictionary<string, (HashSet<string> Keys, DateTime LastUpdated)> _activeMetricKeys = new();
        private Timer? _cleanupTimer;

        public FailureSearchService(IOptions<Settings> settings, ILogger<FailureSearchService> logger, MetricsManager metricsManager, FileHelper fileHelper)
        {
            _settings = settings.Value;
            _logger = logger;
            _metricsManager = metricsManager;
            _fileHelper = fileHelper;

            var cleanupInterval = TimeSpan.FromHours(_settings.CleanupTimerIntervalHours);
            _cleanupTimer = new Timer(state => CleanupOldEntries(state, _logger), _settings, (int)cleanupInterval.TotalMilliseconds, (int)cleanupInterval.TotalMilliseconds);
        }

        public async Task SearchFolderForFailuresAsync(string rootDir, string path, string dName, string env)
        {
            _logger.LogInformation($"Starting scan for failures in root directory: {rootDir}, path: {path}, dName: {dName}, env: {env}");
            var stopwatch = Stopwatch.StartNew();
            var normalizedDName = char.ToUpper(dName[0]) + dName[1..];

            try
            {
                var allFailures = await ScanDirectoryTreeAsync(path, normalizedDName);
                var recentFailures = GetRecentFailures(allFailures);

                await RecordMetricsAsync(allFailures, recentFailures, rootDir, path, normalizedDName, env);
                await SaveFailureReportsAsync(allFailures, recentFailures, path);

                _logger.LogInformation($"Completed scan for {normalizedDName}: {allFailures.Count} total, {recentFailures.Count} recent failures");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning {normalizedDName}");
                throw;
            }
            finally
            {
                RecordScanDuration(rootDir, normalizedDName, env, stopwatch.ElapsedMilliseconds);
                _logger.LogInformation($"Scan completed for {normalizedDName} in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        private async Task<List<FailureReason>> ScanDirectoryTreeAsync(string rootPath, string dName)
        {
            _logger.LogInformation($"Starting ScanDirectoryTreeAsync for root path: {rootPath}, dName: {dName}");
            var allFailures = new List<FailureReason>();
            var queue = new Queue<(string path, int depth)>();
            queue.Enqueue((rootPath, 0));

            while (queue.Count > 0 && allFailures.Count < _settings.MaxFailures)
            {
                var (currentPath, depth) = queue.Dequeue();

                try
                {
                    var (_, failures) = await _fileHelper.NumberOfFaileds(currentPath);
                    allFailures.AddRange(failures);

                    if (ShouldRecurse(dName, depth))
                    {
                        var subdirs = await _fileHelper.GetSubDirectories(currentPath);
                        foreach (var subdir in subdirs)
                        {
                            queue.Enqueue((Path.Combine(currentPath, subdir), depth + 1));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing {currentPath}");
                }
            }
            _logger.LogInformation($"ScanDirectoryTreeAsync finished. Total failures found: {allFailures.Count}");
            return allFailures;
        }

        private List<FailureReason> GetRecentFailures(List<FailureReason> allFailures)
        {
            _logger.LogInformation($"Calculating recent failures from {allFailures.Count} total failures.");
            var cutoff = DateTime.Now.AddHours(-_settings.RecentErrorsTimeWindowHours);
            var recent = allFailures.Where(f => f.LastWriteTime >= cutoff).ToList();
            _logger.LogInformation($"Found {recent.Count} recent failures.");
            return recent;
        }

        private async Task RecordMetricsAsync(List<FailureReason> allFailures, List<FailureReason> recentFailures,
            string rootDir, string path, string dName, string env)
        {
            _logger.LogInformation($"Recording metrics for rootDir: {rootDir}, dName: {dName}, env: {env}");
            RecordGlobalMetrics(allFailures.Count, rootDir, dName, env, false);
            RecordGlobalMetrics(recentFailures.Count, rootDir, dName, env, true);

            bool isGroupedName = _settings.GroupedDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase));

            if (isGroupedName)
            {
                await RecordGroupFolderMetrics(allFailures, rootDir, path, dName, env, false);
                await RecordGroupFolderMetrics(recentFailures, rootDir, path, dName, env, true);
            }
        }

        private async Task RecordGroupFolderMetrics(List<FailureReason> failures, string rootDir, string path, string dName, string env, bool isRecent)
        {
            _logger.LogInformation($"Recording group folder metrics for {dName}, isRecent: {isRecent}. Failures count: {failures.Count}");
            var folderCounts = await GetFolderFailureCounts(failures, path);
            var currentKeys = new HashSet<string>();
            var metricKey = $"{dName}_{isRecent}";

            foreach (var (folderPath, count) in folderCounts)
            {
                if (folderPath.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;

                var folderName = new DirectoryInfo(folderPath).Name;
                var labels = new[] { rootDir, dName, env, folderName, isRecent.ToString().ToLower() };
                var description = $"Failures count in grouped subfolders. The 'is_recent' label indicates if the count is for recent failures (last {_settings.RecentErrorsTimeWindowHours}h) or all failures (false).";

                _metricsManager.SetGaugeValue("n_failures_in_group_folder", description, new[] { "root_dir", "d_name", "env", "group_folder", "is_recent" }, labels, count);

                currentKeys.Add(string.Join('\u0001', labels));
            }

            CleanupStaleMetrics(metricKey, currentKeys);
        }

        private async Task<Dictionary<string, int>> GetFolderFailureCounts(List<FailureReason> failures, string rootPath)
        {
            _logger.LogInformation($"Getting folder failure counts for {failures.Count} failures in root path: {rootPath}");
            var groupFolders = await GetGroupFolders(rootPath);
            var folderCounts = groupFolders.ToDictionary(f => f, _ => 0, StringComparer.OrdinalIgnoreCase);

            foreach (var failure in failures)
            {
                var currentDir = Directory.GetParent(failure.Path);
                while (currentDir != null && currentDir.FullName.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (folderCounts.ContainsKey(currentDir.FullName))
                    {
                        folderCounts[currentDir.FullName]++;
                    }
                    currentDir = currentDir.Parent;
                }
            }
            _logger.LogInformation($"Finished calculating folder failure counts. Found {folderCounts.Count} folders with failures.");
            return folderCounts;
        }

        private async Task<HashSet<string>> GetGroupFolders(string rootPath)
        {
            _logger.LogInformation($"Getting group folders from root path: {rootPath}");
            var groupFolders = new HashSet<string>();

            try
            {
                await Task.Run(() =>
                {
                    using var enumerator = Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                        .Take(_settings.MaxFailures)
                        .GetEnumerator();

                    while (enumerator.MoveNext())
                    {
                        var dir = enumerator.Current;
                        if (Directory.EnumerateDirectories(dir).Any())
                        {
                            groupFolders.Add(dir);
                        }
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting group folders from {rootPath}");
            }
            _logger.LogInformation($"Found {groupFolders.Count} group folders.");
            return groupFolders;
        }

        private void CleanupStaleMetrics(string metricKey, HashSet<string> currentKeys)
        {
            _logger.LogInformation($"Starting CleanupStaleMetrics for metricKey: {metricKey}");
            if (_activeMetricKeys.TryGetValue(metricKey, out var entry))
            {
                var previousKeys = entry.Keys;
                var staleKeys = previousKeys.Except(currentKeys);
                foreach (var staleKey in staleKeys)
                {
                    var labels = staleKey.Split('\u0001');
                    var description = $"Failures count in grouped subfolders. The 'is_recent' label indicates if the count is for recent failures (last {_settings.RecentErrorsTimeWindowHours}h) or all failures (false).";

                    _metricsManager.SetGaugeValue("n_failures_in_group_folder", description, new[] { "root_dir", "d_name", "env", "group_folder", "is_recent" }, labels, 0);
                    _metricsManager.RemoveGaugeSeries("n_failures_in_group_folder", labels);
                }
            }

            _activeMetricKeys[metricKey] = (currentKeys, DateTime.UtcNow);
        }

        private static void CleanupOldEntries(object? state, ILogger<FailureSearchService> logger)
        {
            logger.LogInformation("CleanupOldEntries timer triggered.");
            try
            {
                var currentSettings = (Settings)state!;
                var keysToRemove = _activeMetricKeys
                    .Where(entry => entry.Key.Contains("_") &&
                                 (DateTime.UtcNow - entry.Value.LastUpdated).TotalHours > currentSettings.CleanupTimerIntervalHours)
                    .Select(entry => entry.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _activeMetricKeys.TryRemove(key, out _);
                }

                if (keysToRemove.Count > 0)
                {
                    logger.LogInformation($"Successfully removed {keysToRemove.Count} old metric keys from _activeMetricKeys.");
                }
                else
                {
                    logger.LogInformation("No old metric keys to remove from _activeMetricKeys.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during CleanupOldEntries.");
            }
        }

        private void RecordGlobalMetrics(int count, string rootDir, string dName, string env, bool isRecent)
        {
            _logger.LogInformation($"Recording global metrics for count: {count}, rootDir: {rootDir}, dName: {dName}, env: {env}, isRecent: {isRecent}");
            var description = "Total failures for d_name. The 'is_recent' label indicates if the count is for recent failures (true) or all failures (false).";

            _metricsManager.SetGaugeValue("total_nFailures", description,
                new[] { "root_dir", "d_name", "env", "is_recent" },
                new[] { rootDir, dName, env, isRecent.ToString().ToLower() }, count);
        }

        private void RecordScanDuration(string rootDir, string dName, string env, long milliseconds)
        {
            _logger.LogInformation($"Recording scan duration for rootDir: {rootDir}, dName: {dName}, env: {env}, duration: {milliseconds}ms");
            _metricsManager.SetGaugeValue("dname_scan_duration_ms", "Duration of d_name scan in milliseconds",
                new[] { "root_dir", "d_name", "env" }, new[] { rootDir, dName, env }, milliseconds);
        }

        private async Task SaveFailureReportsAsync(List<FailureReason> allFailures, List<FailureReason> recentFailures, string path)
        {
            _logger.LogInformation($"Saving failure reports to path: {path}. All failures: {allFailures.Count}, Recent failures: {recentFailures.Count}");
            await SaveFailureReasonsToJsonAsync(allFailures, path, "reasons_all.json");
            await SaveFailureReasonsToJsonAsync(recentFailures, path, "reasons_recent.json");
        }

        private async Task SaveFailureReasonsToJsonAsync(List<FailureReason> failures, string outputPath, string fileName)
        {
            _logger.LogInformation($"Saving {fileName} to {outputPath}. Number of failures: {failures.Count}");
            try
            {
                var data = failures.ToDictionary(f => f.Path, f => new
                {
                    reason = f.Reason,
                    image = _fileHelper.FindImageInDirectory(f.Path),
                    lastWriteTime = f.LastWriteTime.ToString("yyyy-MM-ddTHH:mm:ss")
                });

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                await File.WriteAllTextAsync(Path.Combine(outputPath, fileName), json);
                _logger.LogInformation($"Successfully saved {fileName}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving {fileName}");
            }
        }

        private bool ShouldRecurse(string dName, int depth)
        {
            var maxDepth = _settings.GroupedDNnames
                .Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase))
                ? _settings.MaxDepth
                : 1;
            _logger.LogInformation($"ShouldRecurse check for dName: {dName}, depth: {depth}. MaxDepth: {maxDepth}. Result: {depth < maxDepth}");
            return depth < maxDepth;
        }
    }
}
