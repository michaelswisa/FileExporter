using FileExporter.Interface;
using FileExporter.Models;
using Microsoft.Extensions.Options;

namespace FileExporter.Services
{
    public class ZombieSearchService : SearchServiceBase, IZombieSearchService
    {
        public ZombieSearchService(IOptions<Settings> settings, ILogger<ZombieSearchService> logger, IMetricsManager metricsManager, IFileHelper fileHelper, ITraversalService traversalService)
            : base(settings, logger, metricsManager, fileHelper, traversalService)
        {
        }

        public Task SearchFolderForObservedZombiesAsync(string rootDir, string path, string dName, string env) => SearchFolderAsync(rootDir, path, dName, env, ZombieType.Observed);

        public Task SearchFolderForNonObservedZombiesAsync(string rootDir, string path, string dName, string env) => SearchFolderAsync(rootDir, path, dName, env, ZombieType.Non_Observed);

        public override async Task SearchFolderAsync(string rootDir, string path, string dName, string env, object? scanContext = null)
        {
            var zombieType = scanContext as ZombieType?;

            _logger.LogInformation($"Starting ZOMBIE scan for type '{zombieType}' on: {dName}");
            var normalizedDName = char.ToUpper(dName[0]) + dName[1..];

            try
            {
                var report = await TraverseAndAggregateAsync(path, normalizedDName,
                    (currentPath, parentGroups, currentReport) =>
                    ProcessZombiePathAsync(currentPath, parentGroups, currentReport, normalizedDName, zombieType));

                RecordAllMetrics(report, rootDir, path, normalizedDName, env, zombieType);
                _logger.LogInformation($"Completed scan for {dName} ({zombieType}). Total zombies: {report.TotalItemsFound}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning zombies for {dName}");
            }
        }

        #region Private Scan Logic and Metrics
        private async Task<ZombieFolder?> TryFindZombieAsync(string path, ZombieType? zombieType, string dName)
        {
            bool isZombie = false;
            DateTime? lastWrite = null;

            if (zombieType == ZombieType.Observed && await _fileHelper.IsInObservedNotFailed(path))
            {
                var observedFileName = await _fileHelper.GetFileNameContaining(path, "observed");
                if (!string.IsNullOrEmpty(observedFileName))
                {
                    isZombie = true;
                    lastWrite = await _fileHelper.GetFileLastWriteTimeAsync(Path.Combine(path, observedFileName));
                }
            }
            else if (zombieType == ZombieType.Non_Observed && !(await _fileHelper.GetSubDirectories(path)).Any() && await _fileHelper.NotObservedAndNotFailed(path))
            {
                lastWrite = await _fileHelper.GetDirectoryLastWriteTimeAsync(path);
                isZombie = lastWrite.HasValue;
            }

            if (isZombie && lastWrite.HasValue)
            {
                var threshold = _settings.ZombieThresholdsByDName.GetValueOrDefault(dName, _settings.ZombieTimeThresholdMinutes);

                var timeSinceCreation = (DateTime.Now - lastWrite.Value).TotalMinutes;

                if (timeSinceCreation > threshold)
                {
                    _logger.LogDebug($"Zombie detected for dName {dName} at {path}. Age: {timeSinceCreation:F2} mins, Threshold: {threshold} mins.");
                    return new ZombieFolder
                    {
                        Path = path,
                        LastWriteTime = lastWrite.Value,
                        TimeSinceCreation = timeSinceCreation
                    };
                }
                else
                {
                    _logger.LogDebug($"Path {path} is a potentinal zombie but too recent. Age: {timeSinceCreation:F2} mins, Threshold: {threshold}");
                }
            }
            return null;
        }

        private void RecordAllMetrics(ScanReport report, string rootDir, string path, string dName, string env, ZombieType? zombieType)
        {
            RecordGlobalZombieMetrics(report.TotalItemsFound, rootDir, dName, env, false, zombieType);
            RecordGlobalZombieMetrics(report.RecentItemsFound, rootDir, dName, env, true, zombieType);

            if (_settings.DepthGroupDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase)))
            {
                RecordGroupFolderZombieMetrics(report.GroupFolderCountsAll, rootDir, path, dName, env, false, zombieType);
                RecordGroupFolderZombieMetrics(report.GroupFolderCountsRecent, rootDir, path, dName, env, true, zombieType);
            }
        }

        private void RecordGroupFolderZombieMetrics(IReadOnlyDictionary<string, int> folderCounts, string rootDir, string path, string dName, string env, bool isRecent, ZombieType? zombieType)
        {
            var currentKeys = new HashSet<string>();
            var metricKey = $"{dName}_{zombieType}_{isRecent}";

            foreach (var (groupFolderPath, count) in folderCounts.Where(fc => fc.Value > 0))
            {
                if (groupFolderPath.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;

                var fullPathFromRoot = Path.GetRelativePath(rootDir, groupFolderPath).Replace("\\", "/");
                var labels = new[] { rootDir, dName, env, fullPathFromRoot, isRecent.ToString().ToLower(), zombieType.ToString() };
                _metricsManager.SetGaugeValue(
                    "n_zombies_in_group_folder",
                    "Zombies count in grouped subfolders by type.",
                    new[] { "root_dir", "d_name", "env", "group_folder", "is_recent", "zombie_type" },
                    labels!,
                    count
                    );

                currentKeys.Add(string.Join('\u0001', labels));
            }

            CleanupStaleMetrics("n_zombies_in_group_folder", metricKey, currentKeys);
        }

        private void RecordGlobalZombieMetrics(int count, string rootDir, string dName, string env, bool isRecent, ZombieType? zombieType)
        {
            var description = "Total zombies for d_name by type. 'is_recent' is true for recent zombies. 'zombie_type' is 'observed' or 'non_observed'.";
            _metricsManager.SetGaugeValue(
                "total_n_zombies",
                description,
                new[] { "root_dir", "d_name", "env", "is_recent", "zombie_type" },
                new[] { rootDir, dName, env, isRecent.ToString().ToLower(), zombieType.ToString() }!,
                count
                );
        }

        private async Task ProcessZombiePathAsync(string currentPath, List<string> parentGroups, ScanReport currentReport, string dName, ZombieType? zombieType)
        {
            _logger.LogDebug($"Processing zombie path: {currentPath}");

            var zombie = await TryFindZombieAsync(currentPath, zombieType, dName);
            if (zombie != null)
            {
                _logger.LogDebug($"Zombie confirmed at {zombie.Path}. Age: {zombie.TimeSinceCreation:F2} mins");

                currentReport.IncrementTotalItems();
                if (currentReport.TotalItemsFound % _settings.ProgressLogThreshold == 0)
                {
                    _logger.LogInformation($"Zombie scan in progress for dName '{dName}'. Found {currentReport.TotalItemsFound}");
                }

                foreach (var group in parentGroups) 
                {
                    if (!group.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        currentReport.GroupFolderCountsAll.AddOrUpdate(group, 1, (key, count) => count + 1);
                    }
                }

                if (zombie.LastWriteTime >= DateTime.Now.AddHours(-_settings.RecentTimeWindowHours))
                {
                    currentReport.IncrementRecentItems();
                    foreach (var group in parentGroups)
                    {
                        if (!group.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                        {
                            currentReport.GroupFolderCountsRecent.AddOrUpdate(group, 1, (key, count) => count + 1);
                        }
                    }
                }
            }
        }
        #endregion
    }

    public class ZombieFolder : ISearchResult
    {
        public string Path { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
        public double TimeSinceCreation { get; set; }
    }

    public enum ZombieType
    {
        Observed,
        Non_Observed
    }
}
