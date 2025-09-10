using FileExporter.Interface;
using FileExporter.Models;
using Microsoft.Extensions.Options;

namespace FileExporter.Services
{
    public class FileHelper: IFileHelper
    {
        private readonly ILogger<FileHelper> _logger;
        private const string FailedFileSubstring = "fail";
        private const string ObservedFileSubstring = "observed";
        private readonly Settings _settings;
        private readonly HashSet<string> SupportedImageExtensions;

        public FileHelper(ILogger<FileHelper> logger, IOptions<Settings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
            SupportedImageExtensions = new HashSet<string>(_settings.SupportedImageExtensions, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<IEnumerable<string>> GetFilesInPath(string path)
        {
            _logger.LogInformation($"Attempting to enumerate files in path: {path}");
            try
            {
                var files = await Task.Run(() => Directory.EnumerateFileSystemEntries(path));
                _logger.LogDebug($"Successfully created enumerator for files in path: {path}");
                return files;
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogError($"Path: {path} does not exist.");
                return Enumerable.Empty<string>();
            }
            catch (Exception e)
            {
                _logger.LogError($"An error occurred while reading path: {path}. {e.Message}");
                return Enumerable.Empty<string>();
            }
        }

        public async Task<IEnumerable<string>> GetSubDirectories(string path)
        {
            _logger.LogDebug($"Attempting to enumerate subdirectories in path: {path}");
            try
            {
                if (!Directory.Exists(path))
                {
                    _logger.LogError($"Path: {path} does not exists");
                    return Enumerable.Empty<string>();
                }

                var directories = await Task.Run(() => Directory.EnumerateDirectories(path).Select(Path.GetFileName));
                _logger.LogDebug($"Successfully created enumerator for subdirectories in path: {path}");
                return directories!;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Can't get sub-directories. erro: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        public async Task<FailureReason?> ReadFileAsync(string filePath)
        {
            _logger.LogDebug($"Attempting to read file: {filePath}");
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    _logger.LogWarning($"File does not exist: {filePath}");
                    return null;
                }
                if (fileInfo.Length > 1024 * 1024)
                {
                    _logger.LogWarning($"File {filePath} is too large ({fileInfo.Length} bytes), skipping");
                    return null;
                }

                var reasonText = await File.ReadAllTextAsync(filePath);
                _logger.LogDebug($"Successfully read file: {filePath}");

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

        public async Task<FailureReason?> GetSingleFailureReasonAsync(string path)
        {
            _logger.LogDebug($"Attempting to get single failure reason from path: {path}");

            if (!Directory.Exists(path))
            {
                _logger.LogWarning($"Directory does not exist for failure check: {path}");
                return null;
            }

            try
            {
                var failedFilePath = Directory.EnumerateFiles(path).FirstOrDefault(f => Path.GetFileName(f).Contains(FailedFileSubstring, StringComparison.OrdinalIgnoreCase));

                if (failedFilePath == null)
                {
                    return null;
                }

                _logger.LogDebug($"Found failure file at {failedFilePath}. Reading content.");
                return await ReadFileAsync(failedFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while trying to find a failure file in {Path}", path);
                return null;
            }
        }

        public string? FindImageInDirectory(string directoryPath)
        {
            _logger.LogDebug($"Attempting to find image in directory: {directoryPath}");
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

        public async Task<string> GetFileNameContaining(string path, string substring)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogWarning($"Path: {path} does not exist.");
                return string.Empty;
            }

            var filesInPath = await GetFilesInPath(path);

            var file = filesInPath.FirstOrDefault(f => Path.GetFileName(f).Contains(substring, StringComparison.OrdinalIgnoreCase));
            return file != null ? Path.GetFileName(file) : string.Empty;
        }

        public async Task<bool> IsInObservedNotFailed(string path) // CHANGED
        {
            if (!Directory.Exists(path))
            {
                _logger.LogDebug($"Path for IsInObservedNotFailed check does not exist: {path}");
                return false;
            }

            var fileNames = (await GetFilesInPath(path)).Select(f => Path.GetFileName(f));

            return !fileNames.Any(name => name.Contains(FailedFileSubstring, StringComparison.OrdinalIgnoreCase)) &&
                    fileNames.Count(name => name.Contains(ObservedFileSubstring, StringComparison.OrdinalIgnoreCase)) == 1;
        }

        public async Task<DateTime?> GetFileLastWriteTimeAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                return await Task.Run(() => new FileInfo(filePath).LastWriteTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting last write time for {FilePath}", filePath);
                return null;
            }
        }

        public async Task<DateTime?> GetDirectoryLastWriteTimeAsync(string directoryPath)
        {
            try
            {
                if (!Directory.Exists(directoryPath)) return null;
                return await Task.Run(() => new DirectoryInfo(directoryPath).LastWriteTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting last write time for {DirectoryPath}", directoryPath);
                return null;
            }
        }

        public async Task<bool> NotObservedAndNotFailed(string path)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogWarning($"Path for NotObservedAndNotFailed check does not exist: {path}");
                return false;
            }

            var files = await Task.Run(() => Directory.EnumerateFiles(path).ToList());

            bool hasFiles = files.Any();
            if (!hasFiles)
            {
                return false;
            }

            bool hasNoFailde = !files.Any(name => name.Contains(FailedFileSubstring, StringComparison.OrdinalIgnoreCase));
            bool hasNonObserved = !files.Any(name => name.Contains(ObservedFileSubstring, StringComparison.OrdinalIgnoreCase));

            return hasNoFailde && hasNonObserved;
        }
    }
}
