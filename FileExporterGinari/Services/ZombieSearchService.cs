using System.Collections.Concurrent;
using System.Diagnostics;
using FileExporterNew.Models;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Services
{
    /// <summary>
    /// Service for finding "zombie" folders (stale folders that were not processed).
    /// This service has two distinct scan types (observed and non-observed), so it contains
    /// internal refactoring to reduce duplication but does not use the common SearchServiceBase.
    /// </summary>
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

        public Task SearchFolderForObservedZombiesAsync(string rootDir, string path, string dName, string env)
        {
            return SearchFolderForZombiesAsync(rootDir, path, dName, env, "observed");
        }

        public Task SearchFolderForNonObservedZombiesAsync(string rootDir, string path, string dName, string env)
        {
            return SearchFolderForZombiesAsync(rootDir, path, dName, env, "non_observed");
        }

        /// <summary>
        /// A single, private orchestration method to handle both types of zombie scans.
        /// </summary>
        private async Task SearchFolderForZombiesAsync(string rootDir, string path, string dName, string env, string zombieType)
        {
            _logger.LogInformation($"Starting scan for {zombieType} zombies in: {path}, dName: {dName}");
            var stopwatch = Stopwatch.StartNew();
            var normalizedDName = char.ToUpper(dName[0]) + dName[1..];

            try
            {
                var (allZombies, recentZombies) = await ScanDirectoryTreeForZombiesAsync(path, normalizedDName, zombieType);

                await RecordZombieMetricsAsync(allZombies, recentZombies, rootDir, path, normalizedDName, env, zombieType);

                _logger.LogInformation($"Completed {zombieType} zombie scan for {normalizedDName}: {allZombies.Count} total, {recentZombies.Count} recent zombies");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning {zombieType} zombies for {normalizedDName}");
                throw;
            }
            finally
            {
                RecordScanDuration(rootDir, normalizedDName, env, stopwatch.ElapsedMilliseconds, zombieType);
                _logger.LogInformation($"{zombieType} zombie scan completed for {normalizedDName} in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        private async Task<(List<ZombieFolder> allZombies, List<ZombieFolder> recentZombies)> ScanDirectoryTreeForZombiesAsync(string rootPath, string dName, string zombieType)
        {
            _logger.LogInformation($"Scanning for {zombieType} zombies in {rootPath}");
            var allZombies = new List<ZombieFolder>();
            var queue = new Queue<(string path, int depth)>();
            queue.Enqueue((rootPath, 0));

            while (queue.Count > 0 && allZombies.Count < _settings.MaxFailures)
            {
                var (currentPath, depth) = queue.Dequeue();
                try
                {
                    bool isZombie = false;
                    DateTime? lastWrite = null;

                    if (depth > 0)
                    {
                        if (zombieType == "observed" && await _fileHelper.IsInObservedNotFailed(currentPath))
                        {
                            var observedFileName = await _fileHelper.GetFileNameByContains(currentPath, "observed");
                            if (!string.IsNullOrEmpty(observedFileName))
                            {
                                var fileInfo = new FileInfo(Path.Combine(currentPath, observedFileName));
                                isZombie = true;
                                lastWrite = fileInfo.LastWriteTime;
                            }
                        }
                        else if (zombieType == "non_observed" && (await _fileHelper.GetSubDirectories(currentPath)).Length == 0 && await _fileHelper.NotObservedAndNotFailed(currentPath))
                        {
                            var dirInfo = new DirectoryInfo(currentPath);
                            isZombie = true;
                            lastWrite = dirInfo.LastWriteTime;
                        }

                        if (isZombie && lastWrite.HasValue)
                        {
                            var timeSinceCreation = (DateTime.Now - lastWrite.Value).TotalMinutes;
                            if (timeSinceCreation > _settings.ZombieTimeThresholdMinutes)
                            {
                                allZombies.Add(new ZombieFolder
                                {
                                    Path = currentPath,
                                    LastWriteTime = lastWrite.Value,
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
            return (allZombies, recentZombies);
        }

        private List<ZombieFolder> GetRecentZombies(List<ZombieFolder> allZombies)
        {
            var cutoff = DateTime.Now.AddHours(-_settings.RecentTimeWindowHours);
            return allZombies.Where(z => z.LastWriteTime >= cutoff).ToList();
        }

        private async Task RecordZombieMetricsAsync(List<ZombieFolder> allZombies, List<ZombieFolder> recentZombies,
            string rootDir, string path, string dName, string env, string zombieType)
        {
            RecordGlobalZombieMetrics(allZombies.Count, rootDir, dName, env, false, zombieType);
            RecordGlobalZombieMetrics(recentZombies.Count, rootDir, dName, env, true, zombieType);

            if (_settings.GroupedDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase)))
            {
                await RecordGroupFolderZombieMetrics(allZombies, rootDir, path, dName, env, false, zombieType);
                await RecordGroupFolderZombieMetrics(recentZombies, rootDir, path, dName, env, true, zombieType);
            }
        }

        private async Task RecordGroupFolderZombieMetrics(List<ZombieFolder> zombies, string rootDir, string path, string dName, string env, bool isRecent, string zombieType)
        {
            var currentKeys = new HashSet<string>();
            var metricKey = $"{dName}_{zombieType}_{isRecent}";

            var potentialGroupFolders = zombies
                .Select(z => Path.GetDirectoryName(z.Path))
                .Where(p => p != null && !p.Equals(path, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            foreach (var groupFolderPath in potentialGroupFolders)
            {
                if ((await _fileHelper.GetSubDirectories(groupFolderPath)).Length > 0)
                {
                    var fullPathFromRoot = Path.GetRelativePath(rootDir, groupFolderPath).Replace("\\", "/");
                    var count = zombies.Count(z => z.Path.StartsWith(groupFolderPath, StringComparison.OrdinalIgnoreCase));
                    if (count > 0)
                    {
                        var labels = new[] { rootDir, dName, env, fullPathFromRoot, isRecent.ToString().ToLower(), zombieType };
                        _metricsManager.SetGaugeValue("n_zombies_in_group_folder", "Zombies count in grouped subfolders by type.",
                            new[] { "root_dir", "d_name", "env", "group_folder", "is_recent", "zombie_type" }, labels, count);
                        currentKeys.Add(string.Join('\u0001', labels));
                    }
                }
            }

            CleanupStaleZombieMetrics(metricKey, currentKeys);
        }

        private void CleanupStaleZombieMetrics(string metricKey, HashSet<string> currentKeys)
        {
            if (_activeMetricKeys.TryGetValue(metricKey, out var entry))
            {
                var staleKeys = entry.Keys.Except(currentKeys);
                foreach (var staleKey in staleKeys)
                {
                    _metricsManager.RemoveGaugeSeries("n_zombies_in_group_folder", staleKey.Split('\u0001'));
                }
            }
            _activeMetricKeys[metricKey] = (currentKeys, DateTime.UtcNow);
        }

        private void RecordGlobalZombieMetrics(int count, string rootDir, string dName, string env, bool isRecent, string zombieType)
        {
            var description = "Total zombies for d_name by type. 'is_recent' is true for recent zombies. 'zombie_type' is 'observed' or 'non_observed'.";
            _metricsManager.SetGaugeValue("total_n_zombies", description,
                new[] { "root_dir", "d_name", "env", "is_recent", "zombie_type" },
                new[] { rootDir, dName, env, isRecent.ToString().ToLower(), zombieType }, count);
        }

        private void RecordScanDuration(string rootDir, string dName, string env, long milliseconds, string zombieType)
        {
            _metricsManager.SetGaugeValue("dname_zombie_scan_duration_ms", "Duration of d_name zombie scan in milliseconds by type",
                new[] { "root_dir", "d_name", "env", "zombie_type" }, new[] { rootDir, dName, env, zombieType }, milliseconds);
        }

        private bool ShouldRecurse(string dName, int depth)
        {
            var maxDepth = _settings.GroupedDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase)) ? _settings.MaxDepth : 1;
            return depth < maxDepth;
        }
    }

    /// <summary>
    /// Represents a zombie folder found during a scan.
    /// Implements ISearchResult to be compatible with common logic if needed in the future.
    /// </summary>
    public class ZombieFolder : ISearchResult
    {
        public string Path { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
        public double TimeSinceCreation { get; set; }
    }
}