using System.Collections.Concurrent;
using System.Diagnostics;
using FileExporterNew.Models;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Services
{
    public abstract class SearchServiceBase
    {
        protected readonly Settings _settings;
        protected readonly ILogger _logger;
        protected readonly MetricsManager _metricsManager;
        protected readonly FileHelper _fileHelper;
        private static readonly ConcurrentDictionary<string, HashSet<string>> _activeMetricKeys = new();

        protected SearchServiceBase(IOptions<Settings> settings, ILogger logger, MetricsManager metricsManager, FileHelper fileHelper)
        {
            _settings = settings.Value;
            _logger = logger;
            _metricsManager = metricsManager;
            _fileHelper = fileHelper;
        }

        #region Abstract and Virtual Methods
        protected abstract Task<DirectoryScanReport> ScanDirectoryTreeAsync(string rootPath, string dName, object? scanContext);
        protected abstract Task RecordMetricsAsync(DirectoryScanReport allItemsResult, DirectoryScanReport recentItemsResult, string rootDir, string path, string dName, string env, object? scanContext);
        protected abstract void RecordScanDuration(string rootDir, string dName, string env, long milliseconds, object? scanContext);
        protected virtual Task PostScanProcessingAsync(List<ISearchResult> allItems, List<ISearchResult> recentItems, string path, object? scanContext)
        {
            return Task.CompletedTask; // Default implementation does nothing.
        }
        #endregion

        public async Task SearchFolderAsync(string rootDir, string path, string dName, string env, object? scanContext = null)
        {
            _logger.LogInformation($"Starting scan in root directory: {rootDir}, path: {path}, dName: {dName}, env: {env}");
            var stopwatch = Stopwatch.StartNew();
            var normalizedDName = char.ToUpper(dName[0]) + dName[1..];

            try
            {
                var scanResult = await ScanDirectoryTreeAsync(path, normalizedDName, scanContext);

                var recentItems = GetRecentItems(scanResult.FoundItems);
                var recentScanResult = CreateRecentReport(scanResult.GroupFolderCounts.Keys, recentItems);

                await RecordMetricsAsync(scanResult, recentScanResult, rootDir, path, normalizedDName, env, scanContext);
                await PostScanProcessingAsync(scanResult.FoundItems, recentItems, path, scanContext);
                _logger.LogInformation($"Completed scan for {normalizedDName}: {scanResult.FoundItems.Count} total, {recentItems.Count} recent items");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning {normalizedDName}");
                throw;
            }
            finally
            {
                stopwatch.Stop();
                RecordScanDuration(rootDir, normalizedDName, env, stopwatch.ElapsedMilliseconds, scanContext);
                _logger.LogInformation($"Scan completed for {normalizedDName} in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        #region Common Helper Methods

        protected List<ISearchResult> GetRecentItems(List<ISearchResult> allItems)
        {
            var cutoff = DateTime.Now.AddHours(-_settings.RecentTimeWindowHours);
            return allItems.Where(f => f.LastWriteTime >= cutoff).ToList();
        }

        protected bool ShouldRecurse(string dName, int depth)
        {
            var maxDepth = _settings.GroupedDNnames
                .Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase))
                ? _settings.MaxDepth
                : 1;
            return depth < maxDepth;
        }


        protected async Task EnqueueSubdirectoriesAsync(string dName, int depth, string currentPath, List<string> parentGroups, DirectoryScanReport result, Queue<(string path, int depth, List<string> parentGroups)> queue)
        {
            if (ShouldRecurse(dName, depth))
            {
                var subdirs = await _fileHelper.GetSubDirectories(currentPath);
                var currentPathIsGroup = subdirs.Any() && depth > 0;

                if (currentPathIsGroup)
                {
                    result.GroupFolderCounts[currentPath] = 0;
                }

                foreach (var subdir in subdirs)
                {
                    var nextParentGroups = new List<string>(parentGroups);
                    if (currentPathIsGroup)
                    {
                        nextParentGroups.Add(currentPath);
                    }
                    queue.Enqueue((Path.Combine(currentPath, subdir), depth + 1, nextParentGroups));
                }
            }
        }

        protected void CleanupStaleMetrics(string metricName, string metricKey, HashSet<string> currentKeys)
        {
            if (_activeMetricKeys.TryGetValue(metricKey, out var oldKeys))
            {
                var staleKeys = oldKeys.Except(currentKeys);
                foreach (var staleKey in staleKeys)
                {
                    var labels = staleKey.Split('\u0001');
                    _metricsManager.RemoveGaugeSeries(metricName, labels);
                }
            }
            _activeMetricKeys[metricKey] = currentKeys;
        }

        private DirectoryScanReport CreateRecentReport(ICollection<string> allGroupFolderPaths, List<ISearchResult> recentItems)
        {
            var recentReport = new DirectoryScanReport { FoundItems = recentItems };

            if (!recentItems.Any() || !allGroupFolderPaths.Any())
            {
                return recentReport;
            }

            foreach (var path in allGroupFolderPaths)
            {
                recentReport.GroupFolderCounts[path] = 0;
            }

            foreach (var item in recentItems)
            {
                var parentDir = Path.GetDirectoryName(item.Path);
                while (parentDir != null)
                {
                    if (recentReport.GroupFolderCounts.ContainsKey(parentDir))
                    {
                        recentReport.GroupFolderCounts[parentDir]++;
                    }
                    parentDir = Directory.GetParent(parentDir)?.FullName;
                }
            }
            return recentReport;
        }

        #endregion
    }
}