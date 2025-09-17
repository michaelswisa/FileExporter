using FileExporter.Interface;
using FileExporter.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace FileExporter.Services
{
    public abstract class SearchServiceBase : IScanService
    {
        protected readonly Settings _settings;
        protected readonly ILogger _logger;
        protected readonly IMetricsManager _metricsManager;
        protected readonly IFileHelper _fileHelper;
        private static readonly ConcurrentDictionary<string, HashSet<string>> _activeMetricKeys = new();

        protected SearchServiceBase(IOptions<Settings> settings, ILogger logger, IMetricsManager metricsManager, IFileHelper fileHelper)
        {
            _settings = settings.Value;
            _logger = logger;
            _metricsManager = metricsManager;
            _fileHelper = fileHelper;
        }

        public abstract Task SearchFolderAsync(string rootDir, string path, string dName, string env, object? scanContext = null);

        #region Common Helper Methods

        protected async Task<ScanReport> TraverseAndAggregateAsync(
            string rootPath,
            string dName,
            ScanReport report,
            Func<string, List<string>, ScanReport, Task> processPathAsync)
        {
            int maxConcurrency = _settings.MaxConcurrentDirectoryScans;
            using var semaphore = new SemaphoreSlim(maxConcurrency);

            var stack = new Stack<(string path, int depth, List<string> parentGroups)>();
            stack.Push((rootPath, 0, new List<string>()));

            var maxDepth = GetMaxScanDepth(dName);

            while (stack.Count > 0)
            {
                if (report.TotalItemsFound > _settings.MaxFailures)
                {
                    _logger.LogInformation($"Reached MaxFailures limit of {_settings.MaxFailures}. Stopping traversal for {dName}.");
                    break;
                }

                var (currentPath, depth, parentGroups) = stack.Pop();
                try
                {
                    if (depth > 0)
                    {
                        await semaphore.WaitAsync();

                        _ = processPathAsync(currentPath, parentGroups, report)
                            .ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                {
                                    _logger.LogError(t.Exception, "Error processing path in parallel: {CurrentPath}", currentPath);
                                }
                                // This task is done, so it releases its slot.
                                semaphore.Release();
                            });
                    }

                    if (depth < maxDepth)
                    {
                        var subdirs = await _fileHelper.GetSubDirectories(currentPath);
                        foreach (var subdir in subdirs.Reverse())
                        {
                            var nextParentGroups = new List<string>(parentGroups);
                            if (depth == 0)
                            {
                                nextParentGroups.Add(Path.Combine(currentPath, subdir));
                            }
                            stack.Push((Path.Combine(currentPath, subdir), depth + 1, nextParentGroups));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing path during traversal: {CurrentPath}", currentPath);
                }
            }

            for (int i = 0; i < maxConcurrency; i++)
            {
                await semaphore.WaitAsync();
            }

            return report;
        }

        protected int GetMaxScanDepth(string dName)
        {
            var isGroupedDName = _settings.DepthGroupDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase));

            return isGroupedDName ? _settings.MaxDepth : 1;
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

        #endregion
    }
}
