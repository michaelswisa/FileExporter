using System.Text.Json;
using FileExporterNew.Models;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Services
{
    public class FailureSearchService : SearchServiceBase
    {
        public FailureSearchService(IOptions<Settings> settings, ILogger<FailureSearchService> logger, MetricsManager metricsManager, FileHelper fileHelper)
            : base(settings, logger, metricsManager, fileHelper)
        {
        }

        public Task SearchFolderForFailuresAsync(string rootDir, string path, string dName, string env)
        {
            return SearchFolderAsync(rootDir, path, dName, env);
        }

        #region Base Class Overrides

        protected override async Task<List<ISearchResult>> ScanDirectoryTreeAsync(string rootPath, string dName)
        {
            _logger.LogInformation($"Starting failure scan for root path: {rootPath}, dName: {dName}");
            var allFailures = new List<FailureReason>();
            var queue = new Queue<(string path, int depth)>();
            queue.Enqueue((rootPath, 0));

            while (queue.Count > 0 && allFailures.Count < _settings.MaxFailures)
            {
                var (currentPath, depth) = queue.Dequeue();
                try
                {
                    var failures = await _fileHelper.NumberOfFaileds(currentPath);
                    allFailures.AddRange(failures);

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
            _logger.LogInformation($"Failure scan finished. Total failures found: {allFailures.Count}");
            return allFailures.Cast<ISearchResult>().ToList();
        }

        protected override async Task RecordMetricsAsync(List<ISearchResult> allItems, List<ISearchResult> recentItems, string rootDir, string path, string dName, string env)
        {
            // Cast back to the specific type to access unique properties if needed
            var allFailures = allItems.Cast<FailureReason>().ToList();
            var recentFailures = recentItems.Cast<FailureReason>().ToList();

            _logger.LogInformation($"Recording failure metrics for rootDir: {rootDir}, dName: {dName}, env: {env}");
            RecordGlobalMetrics(allFailures.Count, rootDir, dName, env, false);
            RecordGlobalMetrics(recentFailures.Count, rootDir, dName, env, true);

            bool isGroupedName = _settings.GroupedDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase));
            if (isGroupedName)
            {
                await RecordGroupFolderMetrics(allFailures, rootDir, path, dName, env, false);
                await RecordGroupFolderMetrics(recentFailures, rootDir, path, dName, env, true);
            }
        }

        protected override void RecordScanDuration(string rootDir, string dName, string env, long milliseconds)
        {
            _logger.LogInformation($"Recording failure scan duration for rootDir: {rootDir}, dName: {dName}, env: {env}, duration: {milliseconds}ms");
            _metricsManager.SetGaugeValue("dname_scan_duration_ms", "Duration of d_name scan in milliseconds",
                new[] { "root_dir", "d_name", "env" }, new[] { rootDir, dName, env }, milliseconds);
        }

        protected override async Task PostScanProcessingAsync(List<ISearchResult> allItems, List<ISearchResult> recentItems, string path)
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

        private async Task RecordGroupFolderMetrics(List<FailureReason> failures, string rootDir, string path, string dName, string env, bool isRecent)
        {
            _logger.LogInformation($"Recording group folder metrics for {dName}, isRecent: {isRecent}. Failures count: {failures.Count}");

            // 1. קבל את ספירת הכשלים בתיקיות
            var folderCounts = await GetFolderFailureCounts(failures, path);

            var currentKeys = new HashSet<string>();
            var metricKey = $"{dName}_failures_{isRecent}"; // מפתח ייחודי לסוג המדד הזה

            // 2. עבור רק על התיקיות שיש בהן כשלים (ערך גדול מ-0)
            foreach (var (folderPath, count) in folderCounts.Where(fc => fc.Value > 0))
            {
                if (folderPath.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;

                var fullPathFromRoot = Path.GetRelativePath(rootDir, folderPath).Replace("\\", "/");
                var labels = new[] { rootDir, dName, env, fullPathFromRoot, isRecent.ToString().ToLower() };
                var description = $"Failures count in grouped subfolders. The 'is_recent' label indicates if the count is for recent failures (last {_settings.RecentTimeWindowHours}h) or all failures (false).";

                _metricsManager.SetGaugeValue("n_failures_in_group_folder", description, new[] { "root_dir", "d_name", "env", "group_folder", "is_recent" }, labels, count);

                // 3. הוסף למפתחות הנוכחיים רק את אלו שדיווחנו עליהם
                currentKeys.Add(string.Join('\u0001', labels));
            }

            // 4. קרא למתודת הניקוי מהבסיס כדי להסיר את המדדים הישנים
            CleanupStaleMetrics("n_failures_in_group_folder", metricKey, currentKeys);
        }

        private async Task<Dictionary<string, int>> GetFolderFailureCounts(List<FailureReason> failures, string rootPath)
        {
            // This logic is specific to failures and remains here.
            _logger.LogInformation($"Getting folder failure counts for {failures.Count} failures in root path: {rootPath}");
            var groupFolders = await GetGroupFolders(rootPath);
            var folderCounts = groupFolders.ToDictionary(f => f, _ => 0, StringComparer.OrdinalIgnoreCase);

            foreach (var failure in failures)
            {
                var currentDir = Directory.GetParent(failure.Path);
                while (currentDir != null && currentDir.FullName.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (folderCounts.ContainsKey(currentDir.FullName))
                    {
                        folderCounts[currentDir.FullName]++;
                    }
                    currentDir = currentDir.Parent;
                }
            }
            return folderCounts;
        }

        private async Task<HashSet<string>> GetGroupFolders(string rootPath)
        {
            // This logic is specific to failures and remains here.
            var groupFolders = new HashSet<string>();
            try
            {
                await Task.Run(() =>
                {
                    var allDirs = Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories).Take(_settings.MaxFailures);
                    foreach (var dir in allDirs)
                    {
                        if (Directory.EnumerateDirectories(dir).Any())
                        {
                            groupFolders.Add(dir);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting group folders from {rootPath}");
            }
            return groupFolders;
        }

        private async Task SaveFailureReasonsToJsonAsync(List<FailureReason> failures, string outputPath, string fileName)
        {
            try
            {
                var data = failures.ToDictionary(f => f.Path, f => new
                {
                    reason = f.Reason,
                    image = _fileHelper.FindImageInDirectory(f.Path),
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