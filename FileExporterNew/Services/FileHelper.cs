using FileExporterNew.Models;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Services
{
    public class FileHelper
    {
        private readonly ILogger<FileHelper> _logger;
        private const string FailedFileEnding = "fail";
        private const string ObservedFileEnding = "observed";
        private readonly Settings _settings;
        private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp" };

        public FileHelper(ILogger<FileHelper> logger, IOptions<Settings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
        }

        public async Task<string[]> GetFilesInPath(string path)
        {
            _logger.LogInformation("Attempting to get files in path: {Path}", path);
            try
            {
                var files = await Task.Run(() =>
                    Directory.EnumerateFileSystemEntries(path)
                        .Take(_settings.MaxFilesToRead)
                        .ToArray());
                _logger.LogInformation($"Found {files.Length} files in path: {path}");
                return files;
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
            _logger.LogInformation($"Attempting to get subdirectories in path: {path}");
            try
            {
                if (!Directory.Exists(path))
                {
                    _logger.LogError($"Path: {path} does not exists");
                    return Array.Empty<string>();
                }

                var directories = await Task.Run(() => Directory.EnumerateDirectories(path).Take(_settings.MaxFilesToRead));
                var result = directories.Select(d => Path.GetFileName(d)).ToArray();
                _logger.LogInformation($"Found {result.Length} subdirectories in path: {path}");
                return result;
            }
            catch (Exception e)
            {
                _logger.LogError($"Can't get sub-directories. erro: {e.Message}");
                return Array.Empty<string>();
            }
        }

        public async Task<FailureReason?> ReadFileAsync(string filePath)
        {
            _logger.LogInformation($"Attempting to read file: {filePath}");
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    _logger.LogWarning($"File does not exist: {filePath}");
                    return null;
                }

                if (fileInfo.Length > 1024 * 1024) // 1MB limit
                {
                    _logger.LogWarning($"File {filePath} is too large ({fileInfo.Length} bytes), skipping");
                    return null;
                }

                var reasonText = await File.ReadAllTextAsync(filePath);
                _logger.LogInformation($"Successfully read file: {filePath}");
                return new FailureReason
                {
                    Path = Path.GetDirectoryName(filePath) ?? string.Empty,
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
            _logger.LogInformation($"Attempting to get failed files in path: {path}");
            if (!Directory.Exists(path))
            {
                _logger.LogError($"Path: {path} does not exists");
                return new List<FailureReason>();
            }

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
            _logger.LogInformation($"Found {failedFilesList.Count} failed files in path: {path}");
            return failedFilesList;
        }

        public async Task<(int, List<FailureReason>)> NumberOfFaileds(string path)
        {
            _logger.LogInformation($"Calculating number of failed files for path: {path}");
            if (!Directory.Exists(path))
            {
                _logger.LogError($"Path: {path} does not exist for NumberOfFaileds.");
            }

            var failedFiles = await GetFailedFilesAsync(path);
            _logger.LogInformation($"Returning {failedFiles.Count} failed files for path: {path}");
            return (failedFiles.Count, failedFiles);
        }

        public string? FindImageInDirectory(string directoryPath)
        {
            _logger.LogInformation($"Attempting to find image in directory: {directoryPath}");
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            {
                _logger.LogWarning($"Invalid or non-existent directory path provided: {directoryPath}");
                return null;
            }

            try
            {
                var imageFile = Directory.EnumerateFiles(directoryPath).FirstOrDefault(file => SupportedImageExtensions.Contains(Path.GetExtension(file)));

                return imageFile;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error searching for image in directory: {directoryPath}. Error: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetFileNameByEnding(string path, string ending)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogError($"Path: {path} does not exist.");
                return string.Empty;
            }

            string[] filesInPath = await GetFilesInPath(path);
            var file = filesInPath.FirstOrDefault(file => file.EndsWith(ending, StringComparison.OrdinalIgnoreCase));
            return file != null ? Path.GetFileName(file) : string.Empty;
        }

        public async Task<bool> IsInObservedNotFailed(string path)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogError($"Path: {path} does not exist.");
                return false;
            }

            var filesInPath = (await GetFilesInPath(path)).ToList();
            return (
                !filesInPath.Any(x => x.Contains(FailedFileEnding, StringComparison.OrdinalIgnoreCase)) &&
                filesInPath.Count(x => x.Contains(ObservedFileEnding, StringComparison.OrdinalIgnoreCase)) == 1
            );
        }

        public async Task<bool> NotObservedAndNotFailed(string path)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogError($"Path: {path} does not exist.");
                return false;
            }

            var filesInPath = (await GetFilesInPath(path)).ToList();
            return (
                !filesInPath.Any(x => x.Contains(FailedFileEnding, StringComparison.OrdinalIgnoreCase)) &&
                !filesInPath.Any(x => x.Contains(ObservedFileEnding, StringComparison.OrdinalIgnoreCase))
            );
        }
    }
}
