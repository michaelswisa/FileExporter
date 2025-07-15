using System.Collections.Concurrent;
using FileExporterNew.Models;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Services
{
    public class FileHelper
    {
        private readonly ILogger<FileHelper> _logger;
        private const string FailedFileEnding = "fail";
        private readonly Settings _settings;

        public FileHelper(ILogger<FileHelper> logger, IOptions<Settings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
        }

        public async Task<string[]> GetFilesInPath(string path)
        {
            try
            {
                return await Task.Run(() => 
                    Directory.EnumerateFileSystemEntries(path)
                        .Take(_settings.MaxFilesToRead)
                        .ToArray());
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogError($"Path: {path} does not exist.");
                return Array.Empty<string>();
            }
            catch (Exception e)
            {
                _logger.LogError($"An error occurred while reading path: {path}. {e.Message}");
                return Array.Empty<string>();
            }
        }

        public async Task<string[]> GetSubDirectories(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    _logger.LogError($"Path: {path} does not exists");
                    return Array.Empty<string>();
                }

                var directories = await Task.Run(() => Directory.EnumerateDirectories(path).Take(_settings.MaxFilesToRead));
                return directories.Select(d => Path.GetFileName(d)).ToArray();
            }
            catch (Exception e)
            {
                _logger.LogError($"Can't get sub-directories. erro: {e.Message}");
                return Array.Empty<string>();
            }
        }

        public async Task<FailureReason?> ReadFileAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists) return null;

                // Limit file size to prevent memory issues
                if (fileInfo.Length > 1024 * 1024) // 1MB limit
                {
                    _logger.LogWarning($"File {filePath} is too large ({fileInfo.Length} bytes), skipping");
                    return null;
                }

                var reasonText = await File.ReadAllTextAsync(filePath);
                return new FailureReason
                {
                    Path = filePath,
                    Reason = reasonText,
                    LastWriteTime = fileInfo.LastWriteTime
                };
            }
            catch (Exception e)
            {
                _logger.LogError($"Error reading file {filePath}. error: {e.Message}");
                return null;
            }
        }

        public async Task<List<FailureReason>> GetFailedFilesAsync(string path)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogError($"Path: {path} does not exists");
                return new List<FailureReason>();
            }

            // Use streaming approach to avoid loading all files into memory
            var failedFiles = Directory.EnumerateFileSystemEntries(path)
                .Where(x => File.Exists(x) && Path.GetFileName(x).Contains(FailedFileEnding, StringComparison.OrdinalIgnoreCase))
                .Take(_settings.MaxFilesToRead);

            var failedFilesList = new List<FailureReason>();
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);

            var tasks = failedFiles.Select(async filePath =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var failedFile = await ReadFileAsync(filePath);
                    if (failedFile != null)
                    {
                        lock (failedFilesList)
                        {
                            failedFilesList.Add(failedFile);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            semaphore.Dispose();

            return failedFilesList;
        }

        public async Task<(int, List<FailureReason>)> NumberOfFaileds(string path)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogError($"Path: {path} does not exist");
            }

            var failedFiles = await GetFailedFilesAsync(path);
            return (failedFiles.Count, failedFiles);
        }

    }
}
