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
        private readonly ITraversalService _traversalService;
        private static readonly ConcurrentDictionary<string, HashSet<string>> _activeMetricKeys = new();

        protected SearchServiceBase(IOptions<Settings> settings, ILogger logger, IMetricsManager metricsManager, IFileHelper fileHelper, ITraversalService traversalService)
        {
            _settings = settings.Value;
            _logger = logger;
            _metricsManager = metricsManager;
            _fileHelper = fileHelper;
            _traversalService = traversalService;
        }

        public abstract Task SearchFolderAsync(string rootDir, string path, string dName, string env, object? scanContext = null);

        #region Common Helper Methods

        protected Task<ScanReport> TraverseAndAggregateAsync(
        string rootPath,
        string dName,
        Func<string, List<string>, ScanReport, Task> processPathAsync)
        {
            return _traversalService.TraverseAndAggregateAsync(rootPath, dName, processPathAsync);
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
