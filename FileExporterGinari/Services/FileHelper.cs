using FileExporterNew.Models;

namespace FileExporterNew.Services
{
    public class FileHelper
    {
        private readonly ILogger<FileHelper> _logger;
        private const string FailedFileSubstring = "fail";
        private const string ObservedFileSubstring = "observed";
        private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp" };

        public FileHelper(ILogger<FileHelper> logger)
        {
            _logger = logger;
        }

        public async Task<string[]> GetFilesInPath(string path)
        {
            _logger.LogInformation("Attempting to get files in path: {Path}", path);
            try
            {
                var files = await Task.Run(() => Directory.EnumerateFileSystemEntries(path).ToArray());
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

                var directories = await Task.Run(() => Directory.EnumerateDirectories(path));
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
                if (fileInfo.Length > 1024 * 1024)
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
                var failedFilePath = Directory.EnumerateFiles(path)
                    .FirstOrDefault(f => Path.GetFileName(f).Contains(FailedFileSubstring, StringComparison.OrdinalIgnoreCase));

                if (failedFilePath == null)
                {
                    return null;
                }

                _logger.LogInformation($"Found failure file at {failedFilePath}. Reading content.");
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

        public async Task<string> GetFileNameContaining(string path, string substring)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogError($"Path: {path} does not exist.");
                return string.Empty;
            }

            string[] filesInPath = await GetFilesInPath(path);
            
            var file = filesInPath.FirstOrDefault(f => Path.GetFileName(f).Contains(substring, StringComparison.OrdinalIgnoreCase));
            return file != null ? Path.GetFileName(file) : string.Empty;
        }

        public async Task<bool> IsInObservedNotFailed(string path)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogWarning($"Path for IsInObservedNotFailed check does not exist: {path}");
                return false;
            }
            var fileNames = await Task.Run(() =>
                Directory.EnumerateFiles(path)
                         .Select(f => Path.GetFileName(f))
                         .ToList());

            return !fileNames.Any(name => name.Contains(FailedFileSubstring, StringComparison.OrdinalIgnoreCase)) &&
                    fileNames.Count(name => name.Contains(ObservedFileSubstring, StringComparison.OrdinalIgnoreCase)) == 1;
        }

        public async Task<bool> NotObservedAndNotFailed(string path)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogWarning($"Path for NotObservedAndNotFailed check does not exist: {path}");
                return false;
            }
            var fileNames = await Task.Run(() =>
                Directory.EnumerateFiles(path)
                         .Select(f => Path.GetFileName(f))
                         .ToList());

            return !fileNames.Any(name => name.Contains(FailedFileSubstring, StringComparison.OrdinalIgnoreCase)) &&
                   !fileNames.Any(name => name.Contains(ObservedFileSubstring, StringComparison.OrdinalIgnoreCase));
        }
    }
}