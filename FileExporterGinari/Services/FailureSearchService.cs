using System.Text.Json;
using FileExporterNew.Models;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Services
{
    public class FailureSearchService : SearchServiceBase, IFailureSearchService
    {
        public FailureSearchService(IOptions<Settings> settings, ILogger<FailureSearchService> logger, IMetricsManager metricsManager, IFileHelper fileHelper)
            : base(settings, logger, metricsManager, fileHelper)
        {
        }

        public Task SearchFolderForFailuresAsync(string rootDir, string path, string dName, string env)
        {
            return SearchFolderAsync(rootDir, path, dName, env, null);
        }

        #region Base Class Overrides

        protected override async Task<DirectoryScanReport> ScanDirectoryTreeAsync(string rootPath, string dName, object? scanContext)
        {
            _logger.LogInformation($"Starting DRY failure scan for: {rootPath}");

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
                        var failure = await _fileHelper.GetSingleFailureReasonAsync(currentPath);

                        if (failure != null)
                        {
                            failure.Image = _fileHelper.FindImageInDirectory(failure.Path);

                            result.FoundItems.Add(failure);
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

            _logger.LogInformation($"DRY scan finished. Total failures found: {result.FoundItems.Count}");
            return result;
        }

        protected override Task RecordMetricsAsync(DirectoryScanReport allItemsResult, DirectoryScanReport recentItemsResult, string rootDir, string path, string dName, string env, object? scanContext)
        {
            _logger.LogInformation($"Recording failure metrics for rootDir: {rootDir}, dName: {dName}, env: {env}");

            RecordGlobalMetrics(allItemsResult.FoundItems.Count, rootDir, dName, env, false);
            RecordGlobalMetrics(recentItemsResult.FoundItems.Count, rootDir, dName, env, true);

            if (_settings.GroupedDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase)))
            {
                RecordGroupFolderMetrics(allItemsResult.GroupFolderCounts, rootDir, path, dName, env, false);
                RecordGroupFolderMetrics(recentItemsResult.GroupFolderCounts, rootDir, path, dName, env, true);
            }
            return Task.CompletedTask;
        }

        protected override void RecordScanDuration(string rootDir, string dName, string env, long milliseconds, object? scanContext)
        {
            _logger.LogInformation($"Recording failure scan duration for rootDir: {rootDir}, dName: {dName}, env: {env}, duration: {milliseconds}ms");
            _metricsManager.SetGaugeValue("dname_scan_duration_ms", "Duration of d_name scan in milliseconds",
                new[] { "root_dir", "d_name", "env" }, new[] { rootDir, dName, env }, milliseconds);
        }

        protected override async Task PostScanProcessingAsync(List<ISearchResult> allItems, List<ISearchResult> recentItems, string path, object? scanContext)
        {
            var allFailures = allItems.Cast<FailureReason>().ToList();
            var recentFailures = recentItems.Cast<FailureReason>().ToList();

            _logger.LogInformation($"Saving failure reports to path: {path}. All failures: {allFailures.Count}, Recent failures: {recentFailures.Count}");
            await SaveFailureReasonsToJsonAsync(allFailures, path, "reasons_all.json");
            await SaveFailureReasonsToJsonAsync(recentFailures, path, "reasons_recent.json");
        }

        #endregion

        #region Private Helper Methods

        private void RecordGlobalMetrics(int count, string rootDir, string dName, string env, bool isRecent)
        {
            var description = "Total failures for d_name. The 'is_recent' label indicates if the count is for recent failures (true) or all failures (false).";
            _metricsManager.SetGaugeValue("total_nFailures", description,
                new[] { "root_dir", "d_name", "env", "is_recent" },
                new[] { rootDir, dName, env, isRecent.ToString().ToLower() }, count);
        }

        private void RecordGroupFolderMetrics(Dictionary<string, int> folderCounts, string rootDir, string path, string dName, string env, bool isRecent)
        {
            var currentKeys = new HashSet<string>();
            var metricKey = $"{dName}_failures_{isRecent}";

            foreach (var (folderPath, count) in folderCounts.Where(fc => fc.Value > 0))
            {
                if (folderPath.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;

                var fullPathFromRoot = Path.GetRelativePath(rootDir, folderPath).Replace("\\", "/");
                var labels = new[] { rootDir, dName, env, fullPathFromRoot, isRecent.ToString().ToLower() };
                var description = $"Failures count in grouped subfolders. The 'is_recent' label indicates if the count is for recent failures (last {_settings.RecentTimeWindowHours}h) or all failures (false).";

                _metricsManager.SetGaugeValue("n_failures_in_group_folder", description, new[] { "root_dir", "d_name", "env", "group_folder", "is_recent" }, labels, count);
                currentKeys.Add(string.Join('\u0001', labels));
            }
            CleanupStaleMetrics("n_failures_in_group_folder", metricKey, currentKeys);
        }

        private async Task SaveFailureReasonsToJsonAsync(List<FailureReason> failures, string outputPath, string fileName)
        {
            try
            {
                if (failures == null || failures.Count == 0)
                {
                    _logger.LogInformation($"No failures to save for {fileName}");
                    return;
                }

                var data = failures.ToDictionary(f => f.Path, f => new
                {
                    reason = f.Reason,
                    image = f.Image,
                    lastWriteTime = f.LastWriteTime.ToString("yyyy-MM-ddTHH:mm:ss")
                });
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                await File.WriteAllTextAsync(Path.Combine(outputPath, fileName), json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving {fileName}");
            }
        }

        #endregion
    }
}