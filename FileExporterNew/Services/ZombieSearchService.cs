using System.Collections.Concurrent;
using System.Diagnostics;
using FileExporterNew.Models;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Services
{
    public class ZombieSearchService
    {
        private readonly Settings _settings;
        private readonly ILogger<ZombieSearchService> _logger;
        private readonly MetricsManager _metricsManager;
        private readonly FileHelper _fileHelper;
        private static readonly ConcurrentDictionary<string, (HashSet<string> Keys, DateTime LastUpdated)> _activeMetricKeys = new();

        public ZombieSearchService(IOptions<Settings> settings, ILogger<ZombieSearchService> logger, MetricsManager metricsManager, FileHelper fileHelper)
        {
            _settings = settings.Value;
            _logger = logger;
            _metricsManager = metricsManager;
            _fileHelper = fileHelper;
        }

        public async Task SearchFolderForObservedZombiesAsync(string rootDir, string path, string dName, string env)
        {
            _logger.LogInformation($"Starting scan for observed zombies in root directory: {rootDir}, path: {path}, dName: {dName}, env: {env}");
            var stopwatch = Stopwatch.StartNew();
            var normalizedDName = char.ToUpper(dName[0]) + dName[1..];

            try
            {
                var (allZombies, recentZombies) = await ScanDirectoryTreeForObservedZombiesAsync(path, normalizedDName);

                await RecordObservedZombieMetricsAsync(allZombies, recentZombies, rootDir, path, normalizedDName, env);

                _logger.LogInformation($"Completed observed zombie scan for {normalizedDName}: {allZombies.Count} total, {recentZombies.Count} recent zombies");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning observed zombies for {normalizedDName}");
                throw;
            }
            finally
            {
                RecordScanDuration(rootDir, normalizedDName, env, stopwatch.ElapsedMilliseconds, "observed");
                _logger.LogInformation($"Observed zombie scan completed for {normalizedDName} in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        public async Task SearchFolderForNonObservedZombiesAsync(string rootDir, string path, string dName, string env)
        {
            _logger.LogInformation($"Starting scan for non-observed zombies in root directory: {rootDir}, path: {path}, dName: {dName}, env: {env}");
            var stopwatch = Stopwatch.StartNew();
            var normalizedDName = char.ToUpper(dName[0]) + dName[1..];

            try
            {
                var (allZombies, recentZombies) = await ScanDirectoryTreeForNonObservedZombiesAsync(path, normalizedDName);

                await RecordNonObservedZombieMetricsAsync(allZombies, recentZombies, rootDir, path, normalizedDName, env);

                _logger.LogInformation($"Completed non-observed zombie scan for {normalizedDName}: {allZombies.Count} total, {recentZombies.Count} recent zombies");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning non-observed zombies for {normalizedDName}");
                throw;
            }
            finally
            {
                RecordScanDuration(rootDir, normalizedDName, env, stopwatch.ElapsedMilliseconds, "non_observed");
                _logger.LogInformation($"Non-observed zombie scan completed for {normalizedDName} in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        private async Task<(List<ZombieFolder> allZombies, List<ZombieFolder> recentZombies)> ScanDirectoryTreeForObservedZombiesAsync(string rootPath, string dName)
        {
            _logger.LogInformation($"Starting ScanDirectoryTreeForObservedZombiesAsync for root path: {rootPath}, dName: {dName}");
            var allZombies = new List<ZombieFolder>();
            var queue = new Queue<(string path, int depth)>();
            queue.Enqueue((rootPath, 0));

            while (queue.Count > 0 && allZombies.Count < _settings.MaxZombies)
            {
                var (currentPath, depth) = queue.Dequeue();

                try
                {
                    if (depth > 0 && await _fileHelper.IsInObservedNotFailed(currentPath))
                    {
                        var observedFileName = await _fileHelper.GetFileNameByEnding(currentPath, "observed");
                        if (!string.IsNullOrEmpty(observedFileName))
                        {
                            var observedFilePath = Path.Combine(currentPath, observedFileName);
                            var fileInfo = new FileInfo(observedFilePath);
                            var timeSinceCreation = (DateTime.Now - fileInfo.LastWriteTime).TotalMinutes;

            if (timeSinceCreation > _settings.ObservedZombieThresholdMinutes)
                            {
                                allZombies.Add(new ZombieFolder
                                {
                                    Path = currentPath,
                                    LastWriteTime = fileInfo.LastWriteTime,
                                    TimeSinceCreation = timeSinceCreation
                                });
                            }
                        }
                    }
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

            var recentZombies = GetRecentZombies(allZombies);
            _logger.LogInformation($"ScanDirectoryTreeForObservedZombiesAsync finished. Total zombies found: {allZombies.Count}, Recent: {recentZombies.Count}");
            return (allZombies, recentZombies);
        }
        private async Task<(List<ZombieFolder> allZombies, List<ZombieFolder> recentZombies)> ScanDirectoryTreeForNonObservedZombiesAsync(string rootPath, string dName)
        {
            _logger.LogInformation($"Starting ScanDirectoryTreeForNonObservedZombiesAsync for root path: {rootPath}, dName: {dName}");
            var allZombies = new List<ZombieFolder>();
            var queue = new Queue<(string path, int depth)>();
            queue.Enqueue((rootPath, 0));

            while (queue.Count > 0 && allZombies.Count < _settings.MaxZombies)
            {
                var (currentPath, depth) = queue.Dequeue();

                try
                {
                    var subdirs = await _fileHelper.GetSubDirectories(currentPath);
                    if (subdirs.Length == 0 && depth > 0)
                    {
                        if (await _fileHelper.NotObservedAndNotFailed(currentPath))
                        {
                            var dirInfo = new DirectoryInfo(currentPath);
                            var timeSinceCreation = (DateTime.Now - dirInfo.LastWriteTime).TotalMinutes;

            if (timeSinceCreation > _settings.NonObservedZombieThresholdMinutes)
                            {
                                allZombies.Add(new ZombieFolder
                                {
                                    Path = currentPath,
                                    LastWriteTime = dirInfo.LastWriteTime,
                                    TimeSinceCreation = timeSinceCreation
                                });
                            }
                        }
                    }
                    if (ShouldRecurse(dName, depth))
                    {
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

            var recentZombies = GetRecentZombies(allZombies);
            _logger.LogInformation($"ScanDirectoryTreeForNonObservedZombiesAsync finished. Total zombies found: {allZombies.Count}, Recent: {recentZombies.Count}");
            return (allZombies, recentZombies);
        }

        private List<ZombieFolder> GetRecentZombies(List<ZombieFolder> allZombies)
        {
            _logger.LogInformation($"Calculating recent zombies from {allZombies.Count} total zombies.");
            var cutoff = DateTime.Now.AddHours(-_settings.RecentZombiesTimeWindowHours);
            var recent = allZombies.Where(z => z.LastWriteTime >= cutoff).ToList();
            _logger.LogInformation($"Found {recent.Count} recent zombies.");
            return recent;
        }

        private async Task RecordObservedZombieMetricsAsync(List<ZombieFolder> allZombies, List<ZombieFolder> recentZombies,
            string rootDir, string path, string dName, string env)
        {
            _logger.LogInformation($"Recording observed zombie metrics for rootDir: {rootDir}, dName: {dName}, env: {env}");
            RecordGlobalZombieMetrics(allZombies.Count, rootDir, dName, env, false, "observed");
            RecordGlobalZombieMetrics(recentZombies.Count, rootDir, dName, env, true, "observed");

            bool isGroupedName = _settings.GroupedDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase));

            if (isGroupedName)
            {
                await RecordGroupFolderZombieMetrics(allZombies, rootDir, path, dName, env, false, "observed");
                await RecordGroupFolderZombieMetrics(recentZombies, rootDir, path, dName, env, true, "observed");
            }
        }

        private async Task RecordNonObservedZombieMetricsAsync(List<ZombieFolder> allZombies, List<ZombieFolder> recentZombies,
            string rootDir, string path, string dName, string env)
        {
            _logger.LogInformation($"Recording non-observed zombie metrics for rootDir: {rootDir}, dName: {dName}, env: {env}");
            RecordGlobalZombieMetrics(allZombies.Count, rootDir, dName, env, false, "non_observed");
            RecordGlobalZombieMetrics(recentZombies.Count, rootDir, dName, env, true, "non_observed");

            bool isGroupedName = _settings.GroupedDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase));

            if (isGroupedName)
            {
                await RecordGroupFolderZombieMetrics(allZombies, rootDir, path, dName, env, false, "non_observed");
                await RecordGroupFolderZombieMetrics(recentZombies, rootDir, path, dName, env, true, "non_observed");
            }
        }

        private async Task RecordGroupFolderZombieMetrics(List<ZombieFolder> zombies, string rootDir, string path, string dName, string env, bool isRecent, string zombieType)
        {
            _logger.LogInformation($"Recording group folder zombie metrics for {dName}, isRecent: {isRecent}, zombieType: {zombieType}. Zombies count: {zombies.Count}");
            var currentKeys = new HashSet<string>();
            var metricKey = $"{dName}_{zombieType}_{isRecent}";

            // Collect all unique non-leaf parent folders that contain zombies
            var potentialGroupFolders = new HashSet<string>();
            foreach (var zombie in zombies)
            {
                var currentDir = Path.GetDirectoryName(zombie.Path);
                while (currentDir != null && !currentDir.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    potentialGroupFolders.Add(currentDir);
                    currentDir = Path.GetDirectoryName(currentDir);
                }
            }



            foreach (var groupFolderPath in potentialGroupFolders)
            {
                // Check if this folder is a non-leaf folder (i.e., has subdirectories)
                var subdirs = await _fileHelper.GetSubDirectories(groupFolderPath);
                if (subdirs.Length > 0) // Only process non-leaf folders
                {
                    // Calculate the relative path from the initial 'path' (e.g., "mosh")
                    var relativePathFromRoot = Path.GetRelativePath(path, groupFolderPath);
                    var groupFolderLabel = Path.Combine(dName, relativePathFromRoot).Replace("\\", "/"); // Ensure forward slashes

                    // Count zombies within this group folder (including its subfolders)
                    var count = zombies.Count(z => z.Path.StartsWith(groupFolderPath, StringComparison.OrdinalIgnoreCase));

                    if (count > 0)
                    {
                        var labels = new[] { rootDir, dName, env, groupFolderLabel, isRecent.ToString().ToLower(), zombieType };
                        var description = $"Zombies count in grouped subfolders by type. The 'is_recent' label indicates if the count is for recent zombies (last {_settings.RecentZombiesTimeWindowHours}h) or all zombies (false). The 'zombie_type' indicates observed or non_observed.";

                        _metricsManager.SetGaugeValue("n_zombies_in_group_folder", description,
                            new[] { "root_dir", "d_name", "env", "group_folder", "is_recent", "zombie_type" }, labels, count);

                        currentKeys.Add(string.Join('\u0001', labels));
                    }
                }
            }

            CleanupStaleZombieMetrics(metricKey, currentKeys);
        }

        private void CleanupStaleZombieMetrics(string metricKey, HashSet<string> currentKeys)
        {
            _logger.LogInformation($"Starting CleanupStaleZombieMetrics for metricKey: {metricKey}");
            if (_activeMetricKeys.TryGetValue(metricKey, out var entry))
            {
                var previousKeys = entry.Keys;
                var staleKeys = previousKeys.Except(currentKeys);
                foreach (var staleKey in staleKeys)
                {
                    var labels = staleKey.Split('\u0001');
                    var description = $"Zombies count in grouped subfolders by type. The 'is_recent' label indicates if the count is for recent zombies (last {_settings.RecentZombiesTimeWindowHours}h) or all zombies (false). The 'zombie_type' indicates observed or non_observed.";

                    _metricsManager.RemoveGaugeSeries("n_zombies_in_group_folder", labels);
                }
            }

            _activeMetricKeys[metricKey] = (currentKeys, DateTime.UtcNow);
        }

        private void RecordGlobalZombieMetrics(int count, string rootDir, string dName, string env, bool isRecent, string zombieType)
        {
            _logger.LogInformation($"Recording global zombie metrics for count: {count}, rootDir: {rootDir}, dName: {dName}, env: {env}, isRecent: {isRecent}, zombieType: {zombieType}");
            var description = $"Total zombies for d_name by type. The 'is_recent' label indicates if the count is for recent zombies (true) or all zombies (false). The 'zombie_type' indicates observed or non_observed.";

            _metricsManager.SetGaugeValue("total_n_zombies", description,
                new[] { "root_dir", "d_name", "env", "is_recent", "zombie_type" },
                new[] { rootDir, dName, env, isRecent.ToString().ToLower(), zombieType }, count);
        }

        private void RecordScanDuration(string rootDir, string dName, string env, long milliseconds, string zombieType)
        {
            _logger.LogInformation($"Recording zombie scan duration for rootDir: {rootDir}, dName: {dName}, env: {env}, duration: {milliseconds}ms, zombieType: {zombieType}");
            _metricsManager.SetGaugeValue("dname_zombie_scan_duration_ms", "Duration of d_name zombie scan in milliseconds by type",
                new[] { "root_dir", "d_name", "env", "zombie_type" }, new[] { rootDir, dName, env, zombieType }, milliseconds);
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

    public class ZombieFolder
    {
        public string Path { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
        public double TimeSinceCreation { get; set; }
    }
}
