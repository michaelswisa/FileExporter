using System.Collections.Concurrent;
using FileExporterNew.Models;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Services
{
    public class ZombieSearchService : SearchServiceBase
    {
        public ZombieSearchService(IOptions<Settings> settings, ILogger<ZombieSearchService> logger, MetricsManager metricsManager, FileHelper fileHelper)
            : base(settings, logger, metricsManager, fileHelper)
        {
        }

        public Task SearchFolderForObservedZombiesAsync(string rootDir, string path, string dName, string env)
        {
            return SearchFolderAsync(rootDir, path, dName, env, "observed");
        }

        public Task SearchFolderForNonObservedZombiesAsync(string rootDir, string path, string dName, string env)
        {
            return SearchFolderAsync(rootDir, path, dName, env, "non_observed");
        }

        protected override async Task<DirectoryScanReport> ScanDirectoryTreeAsync(string rootPath, string dName, object? scanContext)
        {
            var zombieType = scanContext as string ?? string.Empty;
            _logger.LogInformation($"Starting refactored zombie scan for type '{zombieType}' in: {rootPath}");

            var result = new DirectoryScanReport();
            var queue = new Queue<(string path, int depth, List<string> parentGroups)>();
            queue.Enqueue((rootPath, 0, new List<string>()));

            while (queue.Count > 0 && result.FoundItems.Count < _settings.MaxFailures)
            {
                var (currentPath, depth, parentGroups) = queue.Dequeue();
                try
                {
                    if (depth > 0)
                    {
                        // Pass the dName to the helper function to determine the correct time threshold.
                        var zombie = await TryFindZombieAsync(currentPath, zombieType, dName);
                        if (zombie != null)
                        {
                            result.FoundItems.Add(zombie);
                            foreach (var group in parentGroups)
                            {
                                result.GroupFolderCounts[group]++;
                            }
                        }
                    }

                    await EnqueueSubdirectoriesAsync(dName, depth, currentPath, parentGroups, result, queue);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing {currentPath}");
                }
            }

            return result;
        }

        protected override Task RecordMetricsAsync(DirectoryScanReport allItemsResult, DirectoryScanReport recentItemsResult, string rootDir, string path, string dName, string env, object? scanContext)
        {
            var zombieType = scanContext as string ?? "unknown";

            RecordGlobalZombieMetrics(allItemsResult.FoundItems.Count, rootDir, dName, env, false, zombieType);
            RecordGlobalZombieMetrics(recentItemsResult.FoundItems.Count, rootDir, dName, env, true, zombieType);

            if (_settings.GroupedDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase)))
            {
                RecordGroupFolderZombieMetrics(allItemsResult.GroupFolderCounts, rootDir, path, dName, env, false, zombieType);
                RecordGroupFolderZombieMetrics(recentItemsResult.GroupFolderCounts, rootDir, path, dName, env, true, zombieType);
            }
            return Task.CompletedTask;
        }

        protected override void RecordScanDuration(string rootDir, string dName, string env, long milliseconds, object? scanContext)
        {
            // This will not be called if the call in SearchServiceBase is commented out.
            var zombieType = scanContext as string ?? "unknown";
            _metricsManager.SetGaugeValue("dname_zombie_scan_duration_ms", "Duration of d_name zombie scan in milliseconds by type",
                new[] { "root_dir", "d_name", "env", "zombie_type" }, new[] { rootDir, dName, env, zombieType }, milliseconds);
        }

        #region Private Helper Methods

        /// <summary>
        /// Tries to identify a zombie folder, considering dName-specific time thresholds.
        /// </summary>
        /// <param name="path">The directory path to check.</param>
        /// <param name="zombieType">The type of zombie to look for ("observed" or "non_observed").</param>
        /// <param name="dName">The dName of the service being scanned, used to find a specific threshold.</param>
        /// <returns>A ZombieFolder object if a zombie is found and meets the time threshold, otherwise null.</returns>
        private async Task<ZombieFolder?> TryFindZombieAsync(string path, string zombieType, string dName)
        {
            bool isZombie = false;
            DateTime? lastWrite = null;

            if (zombieType == "observed" && await _fileHelper.IsInObservedNotFailed(path))
            {
                var observedFileName = await _fileHelper.GetFileNameContaining(path, "observed");
                if (!string.IsNullOrEmpty(observedFileName))
                {
                    var fileInfo = new FileInfo(Path.Combine(path, observedFileName));
                    isZombie = true;
                    lastWrite = fileInfo.LastWriteTime;
                }
            }
            else if (zombieType == "non_observed" && (await _fileHelper.GetSubDirectories(path)).Length == 0 && await _fileHelper.NotObservedAndNotFailed(path))
            {
                var dirInfo = new DirectoryInfo(path);
                isZombie = true;
                lastWrite = dirInfo.LastWriteTime;
            }

            if (isZombie && lastWrite.HasValue)
            {
                // Get the specific threshold for the dName, or fall back to the default.
                var threshold = _settings.ZombieThresholdsByDName.GetValueOrDefault(
                    dName,
                    _settings.ZombieTimeThresholdMinutes);

                var timeSinceCreation = (DateTime.Now - lastWrite.Value).TotalMinutes;

                if (timeSinceCreation > threshold)
                {
                    _logger.LogDebug("Zombie detected for dName {dName} at {path}. Age: {age:F2} mins, Threshold: {threshold} mins.", dName, path, timeSinceCreation, threshold);
                    return new ZombieFolder
                    {
                        Path = path,
                        LastWriteTime = lastWrite.Value,
                        TimeSinceCreation = timeSinceCreation
                    };
                }
            }

            return null; // No zombie found or it's not old enough.
        }

        private void RecordGroupFolderZombieMetrics(Dictionary<string, int> folderCounts, string rootDir, string path, string dName, string env, bool isRecent, string zombieType)
        {
            var currentKeys = new HashSet<string>();
            var metricKey = $"{dName}_{zombieType}_{isRecent}";

            foreach (var (groupFolderPath, count) in folderCounts.Where(fc => fc.Value > 0))
            {
                if (groupFolderPath.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;

                var fullPathFromRoot = Path.GetRelativePath(rootDir, groupFolderPath).Replace("\\", "/");
                var labels = new[] { rootDir, dName, env, fullPathFromRoot, isRecent.ToString().ToLower(), zombieType };
                _metricsManager.SetGaugeValue("n_zombies_in_group_folder", "Zombies count in grouped subfolders by type.",
                    new[] { "root_dir", "d_name", "env", "group_folder", "is_recent", "zombie_type" }, labels, count);
                currentKeys.Add(string.Join('\u0001', labels));
            }

            CleanupStaleMetrics("n_zombies_in_group_folder", metricKey, currentKeys);
        }

        private void RecordGlobalZombieMetrics(int count, string rootDir, string dName, string env, bool isRecent, string zombieType)
        {
            var description = "Total zombies for d_name by type. 'is_recent' is true for recent zombies. 'zombie_type' is 'observed' or 'non_observed'.";
            _metricsManager.SetGaugeValue("total_n_zombies", description,
                new[] { "root_dir", "d_name", "env", "is_recent", "zombie_type" },
                new[] { rootDir, dName, env, isRecent.ToString().ToLower(), zombieType }, count);
        }

        #endregion
    }

    public class ZombieFolder : ISearchResult
    {
        public string Path { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
        public double TimeSinceCreation { get; set; }
    }
}