using FileExporter.Interface;
using FileExporter.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FileExporter.Services
{
    public class FailureSearchService : SearchServiceBase, IFailureSearchService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public FailureSearchService(IOptions<Settings> settings, ILogger<FailureSearchService> logger, IMetricsManager metricsManager, IFileHelper fileHelper)
            : base(settings, logger, metricsManager, fileHelper)
        {
        }

        public Task SearchFolderForFailuresAsync(string rootDir, string path, string dName, string env)
        {
            return SearchFolderAsync(rootDir, path, dName, env, null);
        }

        public override async Task SearchFolderAsync(string rootDir, string path, string dName, string env, object? scanContext = null)
        {
            _logger.LogInformation($"Starting FAILURE scan for: {dName}");
            var normalizedDName = char.ToUpper(dName[0]) + dName[1..];

            var allReasonsPath = Path.Combine(path, "reasons_all.json");
            var recentReasonsPath = Path.Combine(path, "reasons_recent.json");
            var allReasonsTempPath = allReasonsPath + ".tmp";
            var recentReasonsTempPath = recentReasonsPath + ".tmp";

            try
            {
                ScanReport report;
                {
                    using var allFs = new FileStream(allReasonsTempPath, FileMode.Create, FileAccess.Write);
                    using var allWriter = new Utf8JsonWriter(allFs, new JsonWriterOptions { Indented = true });
                    using var recentFs = new FileStream(recentReasonsTempPath, FileMode.Create, FileAccess.Write);
                    using var recentWriter = new Utf8JsonWriter(recentFs, new JsonWriterOptions { Indented = true });

                    allWriter.WriteStartObject();
                    recentWriter.WriteStartObject();
                    var scanContextForTraversal = new FailureScanContext(allWriter, recentWriter, normalizedDName);

                    report = await TraverseAndAggregateAsync(path, normalizedDName, new ScanReport(),
                            (currentPath, parentGroups, currentReport) =>
                            ProcessFailurePathAsync(currentPath, parentGroups, currentReport, scanContextForTraversal));

                    allWriter.WriteEndObject();
                    await allWriter.FlushAsync();
                    recentWriter.WriteEndObject();
                    await recentWriter.FlushAsync();
                }

                File.Move(allReasonsTempPath, allReasonsPath, overwrite: true);
                File.Move(recentReasonsTempPath, recentReasonsPath, overwrite: true);

                RecordAllMetrics(report, rootDir, path, normalizedDName, env);
                _logger.LogInformation($"Completed scan for {dName}. Total failures: {report.TotalItemsFound}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Critical error during scan for {dName}. Aborting.");
            }
        }

        #region Private Scan Logic and Metrics
        private record FailureScanContext(Utf8JsonWriter AllWriter, Utf8JsonWriter RecentWriter, string DName);

        private void RecordAllMetrics(ScanReport report, string rootDir, string path, string dName, string env)
        {
            _logger.LogInformation($"Recording failure metrics for rootDir: {rootDir}, dName: {dName}, env: {env}");

            RecordGlobalMetrics(report.TotalItemsFound, rootDir, dName, env, false);
            RecordGlobalMetrics(report.RecentItemsFound, rootDir, dName, env, true);

            if (_settings.DepthGroupDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase)))
            {
                RecordGroupFolderMetrics(report.GroupFolderCountsAll, rootDir, path, dName, env, false);
                RecordGroupFolderMetrics(report.GroupFolderCountsRecent, rootDir, path, dName, env, true);
            }
        }

        private void RecordGlobalMetrics(int count, string rootDir, string dName, string env, bool isRecent)
        {
            var description = "Total failures for d_name. The 'is_recent' label indicates if the count is for recent failures (true) or all failures (false).";
            _metricsManager.SetGaugeValue(
                "total_nFailures",
                description,
                new[] { "root_dir", "d_name", "env", "is_recent" },
                new[] { rootDir, dName, env, isRecent.ToString().ToLower() }, 
                count
                );
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

        private async Task ProcessFailurePathAsync(string currentPath, List<string> parentGroups, ScanReport currentReport, FailureScanContext context)
        {
            _logger.LogDebug("Processing failure path: {CurrentPath}", currentPath);

            var failure = await _fileHelper.GetSingleFailureReasonAsync(currentPath);
            if (failure != null)
            {
                _logger.LogDebug("Failure found at {Path}. LastWriteTime: {LastWriteTime}", failure.Path, failure.LastWriteTime);

                failure.Image = _fileHelper.FindImageInDirectory(failure.Path);

                currentReport.TotalItemsFound++;
                if (currentReport.TotalItemsFound % _settings.ProgressLogThreshold == 0)
                {
                    _logger.LogInformation($"Failure scan in progress for dName '{context.DName}'. Found {currentReport.TotalItemsFound} failures so far...");
                }

                foreach (var group in parentGroups) 
                {
                    if (!group.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        currentReport.GroupFolderCountsAll[group] = currentReport.GroupFolderCountsAll.GetValueOrDefault(group) + 1;
                    }
                }

                context.AllWriter.WritePropertyName(failure.Path);
                JsonSerializer.Serialize(context.AllWriter, new { reason = failure.Reason, image = failure.Image, lastWriteTime = failure.LastWriteTime }, _jsonOptions);

                if (failure.LastWriteTime >= DateTime.Now.AddHours(-_settings.RecentTimeWindowHours))
                {
                    currentReport.RecentItemsFound++;
                    foreach (var group in parentGroups) 
                    {
                        if (!group.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                        {
                            currentReport.GroupFolderCountsRecent[group] = currentReport.GroupFolderCountsRecent.GetValueOrDefault(group) + 1;
                        }
                    }
                    context.RecentWriter.WritePropertyName(failure.Path);
                    JsonSerializer.Serialize(context.RecentWriter, new { reason = failure.Reason, image = failure.Image, lastWriteTime = failure.LastWriteTime }, _jsonOptions);
                }
            }
        }
        #endregion
    }
}
