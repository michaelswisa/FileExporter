using FileExporter.Interface;
using FileExporter.Models;
using Microsoft.Extensions.Options;

namespace FileExporter.Services
{
    public class TranscodedSearchService : SearchServiceBase, ITranscodedSearchService
    {
        public TranscodedSearchService(IOptions<Settings> settings, ILogger<TranscodedSearchService> logger, IMetricsManager metricsManager, IFileHelper fileHelper, ITraversalService traversalService)
            : base(settings, logger, metricsManager, fileHelper, traversalService)
        {
        }

        public Task SearchFoldersForTranscodedAsync(string rootDir, string path, string dName, string env)
        {
            return SearchFolderAsync(rootDir, path, dName, env, null);
        }

        public override async Task SearchFolderAsync(string rootDir, string path, string dName, string env, object? scanContext = null)
        {
            _logger.LogInformation($"Starting TRANSCODED scan for: {dName}");
            var normalizedDName = char.ToUpper(dName[0]) + dName[1..];

            try
            {
                var (totalCount, recentCount) = await ScanAndCountTranscodedAsync(path, normalizedDName);

                _logger.LogInformation($"Recording transcoded metrics for {dName}. Total: {totalCount}, Recent: {recentCount}");

                var description = $"Count of transcoded folders that contain files. The 'is_recent' label is true for folders with files modified in the last {_settings.RecentTimeWindowHours} hours, and false for the total count.";
                var labelNames = new[] { "root_dir", "d_name", "env", "is_recent" };

                _metricsManager.SetGaugeValue("total_transcoded_folders", description, labelNames, new[] { rootDir, normalizedDName, env, "false" }, totalCount);
                _metricsManager.SetGaugeValue("total_transcoded_folders", description, labelNames, new[] { rootDir, normalizedDName, env, "true" }, recentCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning transcoded for {dName}");
            }
        }

        #region Private Scan Logic
        private async Task<(int totalCount, int recentCount)> ScanAndCountTranscodedAsync(string rootPath, string dName)
        {
            _logger.LogInformation($"Starting transcoded folders count in path: {rootPath}");
            int totalCount = 0;
            int recentCount = 0;
            var cutoff = DateTime.Now.AddHours(-_settings.RecentTimeWindowHours);

            if (!Directory.Exists(rootPath))
            {
                _logger.LogWarning($"The specified path '{rootPath}' for dName '{dName}' does not exist. Skipping scan.");
                return (0, 0);
            }

            var subDirectories = await _fileHelper.GetSubDirectories(rootPath);

            foreach (var subDirName in subDirectories)
            {
                try
                {
                    var subDirPath = Path.Combine(rootPath, subDirName);
                    var fileWriteTime = await GetSingleFileWriteTimeAsync(subDirPath);

                    if (fileWriteTime.HasValue)
                    {
                        totalCount++;
                        if (fileWriteTime.Value >= cutoff)
                        {
                            recentCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing subdirectory {subDirName} in {rootPath}");
                }
            }

            return (totalCount, recentCount);
        }

        private async Task<DateTime?> GetSingleFileWriteTimeAsync(string directoryPath)
        {
            var files = await _fileHelper.GetFilesInPath(directoryPath);
            var firstFile = files.FirstOrDefault();

            if (firstFile == null)
            {
                return null;
            }

            try
            {
                return new FileInfo(firstFile).LastWriteTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting FileInfo for a file in directory {directoryPath}");
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
