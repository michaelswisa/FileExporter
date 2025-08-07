using FileExporterNew.Models;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Services
{
    public class TranscodedSearchService : SearchServiceBase
    {
        public TranscodedSearchService(IOptions<Settings> settings, ILogger<TranscodedSearchService> logger, MetricsManager metricsManager, FileHelper fileHelper)
            : base(settings, logger, metricsManager, fileHelper)
        {
        }

        public Task SearchFoldersForTranscodedAsync(string rootDir, string path, string dName, string env)
        {
            return SearchFolderAsync(rootDir, path, dName, env, null);
        }

        #region Base Class Overrides

        protected override async Task<DirectoryScanReport> ScanDirectoryTreeAsync(string rootPath, string dName, object? scanContext)
        {
            _logger.LogInformation($"Starting transcoded folders scan in path: {rootPath}");
            var result = new DirectoryScanReport();

            if (!Directory.Exists(rootPath))
            {
                _logger.LogWarning($"The specified path '{rootPath}' for dName '{dName}' does not exist. Skipping scan.");
                return result;
            }

            var subDirectories = await _fileHelper.GetSubDirectories(rootPath);
            _logger.LogDebug($"Found {subDirectories.Length} subdirectories to check in '{rootPath}'.");

            foreach (var subDirName in subDirectories)
            {
                try
                {
                    var subDirPath = Path.Combine(rootPath, subDirName);

                    var fileWriteTime = await GetSingleFileWriteTimeAsync(subDirPath);

                    if (fileWriteTime.HasValue)
                    {
                        result.FoundItems.Add(new TranscodedFolderInfo
                        {
                            Path = subDirPath,
                            LastWriteTime = fileWriteTime.Value
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing subdirectory {subDirName} in {rootPath}");
                }
            }

            return result;
        }

        protected override Task RecordMetricsAsync(DirectoryScanReport allItemsResult, DirectoryScanReport recentItemsResult, string rootDir, string path, string dName, string env, object? scanContext)
        {
            var totalCount = allItemsResult.FoundItems.Count;
            var recentCount = recentItemsResult.FoundItems.Count;

            _logger.LogInformation($"Recording transcoded metrics for {dName}. Total: {totalCount}, Recent: {recentCount}");

            var description = $"Count of transcoded folders that contain files. The 'is_recent' label is true for folders with files modified in the last {_settings.RecentTimeWindowHours} hours, and false for the total count.";
            var labelNames = new[] { "root_dir", "d_name", "env", "is_recent" };

            _metricsManager.SetGaugeValue("total_transcoded_folders", description,
                labelNames, new[] { rootDir, dName, env, "false" }, totalCount);

            _metricsManager.SetGaugeValue("total_transcoded_folders", description,
                labelNames, new[] { rootDir, dName, env, "true" }, recentCount);

            return Task.CompletedTask;
        }

        protected override void RecordScanDuration(string rootDir, string dName, string env, long milliseconds, object? scanContext)
        {
            _metricsManager.SetGaugeValue("dname_transcoded_scan_duration_ms", "Duration of the transcoded folder scan in milliseconds.",
                new[] { "root_dir", "d_name", "env" }, new[] { rootDir, dName, env }, milliseconds);
        }

        #endregion

        #region Private Helper Methods

        private async Task<DateTime?> GetSingleFileWriteTimeAsync(string directoryPath)
        {
            var files = await _fileHelper.GetFilesInPath(directoryPath);

            if (files.Length == 0)
            {
                return null;
            }

            if (files.Length > 1)
            {
                _logger.LogWarning($"Expected one file in directory {directoryPath}, but found {files.Length}. Using the first file found.");
            }

            try
            {
                var filePath = files[0];
                return new FileInfo(filePath).LastWriteTime;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting FileInfo for a file in directory {directoryPath}, Error: {ex}");
                return null;
            }
        }

        #endregion
    }

    public class TranscodedFolderInfo : ISearchResult
    {
        public string Path { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
    }
}