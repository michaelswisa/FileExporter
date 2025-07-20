using System.Diagnostics;
using FileExporterNew.Models;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Services
{
    public class TranscodedSearchService
    {
        private readonly Settings _settings;
        private readonly ILogger<TranscodedSearchService> _logger;
        private readonly MetricsManager _metricsManager;
        private readonly FileHelper _fileHelper;

        public TranscodedSearchService(IOptions<Settings> settings, ILogger<TranscodedSearchService> logger, MetricsManager metricsManager, FileHelper fileHelper)
        {
            _settings = settings.Value;
            _logger = logger;
            _metricsManager = metricsManager;
            _fileHelper = fileHelper;
        }

        public async Task SearchFoldersForTranscodedAsync(string rootDir, string path, string dName, string env)
        {
            _logger.LogInformation($"Starting scan for transcoded folders in path: {path} (dName: {dName}, Env: {env})");
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (!Directory.Exists(path))
                {
                    _logger.LogWarning($"The specified path '{path}' for dName '{dName}' does not exist. Skipping scan.");
                    return;
                }

                var (allFoldersWithFiles, recentFoldersWithFiles) = await ScanForFoldersWithFilesAsync(path);

                RecordTranscodedMetrics(allFoldersWithFiles.Count, recentFoldersWithFiles.Count, rootDir, dName, env);

                _logger.LogInformation($"Completed transcoded scan for {dName}: {allFoldersWithFiles.Count} total folders with files, {recentFoldersWithFiles.Count} recent folders.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error scanning for transcoded folders for dName {dName} in path {path}, Error: {ex}");
                throw;
            }
            finally
            {
                RecordScanDuration(rootDir, dName, env, stopwatch.ElapsedMilliseconds);
                _logger.LogInformation($"Transcoded scan for {dName} completed in {stopwatch.ElapsedMilliseconds}ms.");
            }
        }

        private async Task<(List<TranscodedFolderInfo> allFolders, List<TranscodedFolderInfo> recentFolders)> ScanForFoldersWithFilesAsync(string rootPath)
        {
            var allFoldersWithFiles = new List<TranscodedFolderInfo>();

            // Get all immediate subdirectories (the "leaf folders" in this context)
            var subDirectories = await _fileHelper.GetSubDirectories(rootPath);
            _logger.LogDebug($"Found {subDirectories.Length} subdirectories to check in '{rootPath}'.");

            foreach (var subDirName in subDirectories)
            {
                try
                {
                    var subDirPath = Path.Combine(rootPath, subDirName);
                    var mostRecentWriteTime = await GetMostRecentFileWriteTimeAsync(subDirPath);

                    // If the method returns a time, it means the folder contains at least one file.
                    if (mostRecentWriteTime.HasValue)
                    {
                        allFoldersWithFiles.Add(new TranscodedFolderInfo
                        {
                            Path = subDirPath,
                            MostRecentFileWriteTime = mostRecentWriteTime.Value
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing subdirectory {subDirName} in {rootPath}, Error: {ex}");
                }
            }

            // Determine which of the folders are "recent" based on settings
            var cutoff = DateTime.Now.AddHours(-_settings.RecentTimeWindowHours);
            var recentFolders = allFoldersWithFiles
                .Where(f => f.MostRecentFileWriteTime >= cutoff)
                .ToList();

            return (allFoldersWithFiles, recentFolders);
        }

        private async Task<DateTime?> GetMostRecentFileWriteTimeAsync(string directoryPath)
        {
            var files = await _fileHelper.GetFilesInPath(directoryPath);
            if (files.Length == 0)
            {
                return null; // No files in the directory
            }

            // Find the maximum LastWriteTime among all files in the directory
            return files.Select(f => new FileInfo(f).LastWriteTime).Max();
        }

        private void RecordTranscodedMetrics(int totalCount, int recentCount, string rootDir, string dName, string env)
        {
            var description = $"Count of transcoded folders that contain files. The 'is_recent' label is true for folders with files modified in the last {_settings.RecentTimeWindowHours} hours, and false for the total count.";
            var labelNames = new[] { "root_dir", "d_name", "env", "is_recent" };

            // Record total count
            _metricsManager.SetGaugeValue("total_transcoded_folders", description,
                labelNames, new[] { rootDir, dName, env, "false" }, totalCount);

            // Record recent count
            _metricsManager.SetGaugeValue("total_transcoded_folders", description,
                labelNames, new[] { rootDir, dName, env, "true" }, recentCount);
        }

        private void RecordScanDuration(string rootDir, string dName, string env, long milliseconds)
        {
            _metricsManager.SetGaugeValue("dname_transcoded_scan_duration_ms", "Duration of the transcoded folder scan in milliseconds.",
                new[] { "root_dir", "d_name", "env" }, new[] { rootDir, dName, env }, milliseconds);
        }
    }

    public class TranscodedFolderInfo
    {
        public string Path { get; set; } = string.Empty;
        public DateTime MostRecentFileWriteTime { get; set; }
    }
}