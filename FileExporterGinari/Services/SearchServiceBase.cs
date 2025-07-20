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
        private static readonly ConcurrentDictionary<string, (HashSet<string> Keys, DateTime LastUpdated)> _activeMetricKeys = new();

        protected SearchServiceBase(IOptions<Settings> settings, ILogger logger, MetricsManager metricsManager, FileHelper fileHelper)
        {
            _settings = settings.Value;
            _logger = logger;
            _metricsManager = metricsManager;
            _fileHelper = fileHelper;
        }

        #region Abstract and Virtual Methods (To be implemented by derived classes)

        /// <summary>
        /// Scans the directory tree to find specific items. Must be implemented by the derived class.
        /// </summary>
        protected abstract Task<List<ISearchResult>> ScanDirectoryTreeAsync(string rootPath, string dName);

        /// <summary>
        /// Records all relevant metrics for the found items. Must be implemented by the derived class.
        /// </summary>
        protected abstract Task RecordMetricsAsync(List<ISearchResult> allItems, List<ISearchResult> recentItems, string rootDir, string path, string dName, string env);

        /// <summary>
        /// Records the duration of the scan. Must be implemented by the derived class.
        /// </summary>
        protected abstract void RecordScanDuration(string rootDir, string dName, string env, long milliseconds);

        /// <summary>
        /// A hook for derived classes to perform actions after the scan and metrics recording is complete (e.g., save a report).
        /// </summary>
        protected virtual Task PostScanProcessingAsync(List<ISearchResult> allItems, List<ISearchResult> recentItems, string path)
        {
            return Task.CompletedTask; // Default implementation does nothing.
        }

        #endregion

        /// <summary>
        /// The main orchestration method (Template Method) that drives the scanning process.
        /// </summary>
        public async Task SearchFolderAsync(string rootDir, string path, string dName, string env)
        {
            _logger.LogInformation($"Starting scan in root directory: {rootDir}, path: {path}, dName: {dName}, env: {env}");
            var stopwatch = Stopwatch.StartNew();
            var normalizedDName = char.ToUpper(dName[0]) + dName[1..];

            try
            {
                var allItems = await ScanDirectoryTreeAsync(path, normalizedDName);
                var recentItems = GetRecentItems(allItems);

                await RecordMetricsAsync(allItems, recentItems, rootDir, path, normalizedDName, env);
                await PostScanProcessingAsync(allItems, recentItems, path);

                _logger.LogInformation($"Completed scan for {normalizedDName}: {allItems.Count} total, {recentItems.Count} recent items");
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

        #region Common Helper Methods

        /// <summary>
        /// Filters a list of items to include only those modified within the configured time window.
        /// </summary>
        private List<ISearchResult> GetRecentItems(List<ISearchResult> allItems)
        {
            _logger.LogInformation($"Calculating recent items from {allItems.Count} total items.");
            var cutoff = DateTime.Now.AddHours(-_settings.RecentTimeWindowHours);
            return allItems.Where(f => f.LastWriteTime >= cutoff).ToList();
        }

        /// <summary>
        /// Determines whether to recurse into subdirectories based on dName and depth settings.
        /// </summary>
        protected bool ShouldRecurse(string dName, int depth)
        {
            var maxDepth = _settings.GroupedDNnames
                .Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase))
                ? _settings.MaxDepth
                : 1;
            return depth < maxDepth;
        }

        /// <summary>
        /// Removes stale Prometheus gauge series for a given metric.
        /// </summary>
        protected void CleanupStaleMetrics(string metricName, string metricKey, HashSet<string> currentKeys)
        {
            _logger.LogInformation($"Starting CleanupStaleMetrics for metric: {metricName}, key: {metricKey}");
            if (_activeMetricKeys.TryGetValue(metricKey, out var entry))
            {
                var staleKeys = entry.Keys.Except(currentKeys);
                foreach (var staleKey in staleKeys)
                {
                    var labels = staleKey.Split('\u0001');
                    _metricsManager.RemoveGaugeSeries(metricName, labels);
                }
            }
            _activeMetricKeys[metricKey] = (currentKeys, DateTime.UtcNow);
        }

        #endregion
    }
}