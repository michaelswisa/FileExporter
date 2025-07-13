using System.Collections.Concurrent;
using FileExporterNew.Models;

namespace FileExporterNew.Services
{
    public class FileHelper
    {
        private readonly ILogger<FileHelper> _logger;
        private const string FailedFileEnding = "fail";
        private readonly Settings _settings;

        public FileHelper(ILogger<FileHelper> logger, Settings settings)
        {
            _logger = logger;
            _settings = settings;
        }

        public async Task<string[]> GetFilesInPath(string path)
        {
            try
            {
                return await Task.Run(() => Directory.GetFileSystemEntries(path));
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogError($"Path: {path} does not exist.");
                return new string[0];
            }
            catch (Exception e)
            {
                _logger.LogError($"An error occurred while reading path: {path}. {e.Message}");
                return new string[0];
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

        public async Task<FailursReason?> ReadFileAsync(string filePath)
        {
            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    return new FailursReason { Path = filePath, Reason = await reader.ReadToEndAsync() };
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error reading file {filePath}. error: {e.Message}");
                return null;
            }
        }

        public async Task<List<FailursReason>> GetFailedFilesAsync(string path)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogError($"Path: {path} does not exists");
                return new List<FailursReason>();
            }

            string[] filesInPath = await GetFilesInPath(path);
            string[] failedFiles = filesInPath.Where(x => x.EndsWith(FailedFileEnding, StringComparison.OrdinalIgnoreCase)).ToArray();
            var failedFilesList = new ConcurrentBag<FailursReason>();

            await Parallel.ForEachAsync(failedFiles, async (file, cancellationToken) =>
            {
                string filePath = Path.Combine(path, file);
                var failedFile = await ReadFileAsync(filePath);
                if (failedFile != null)
                {
                    failedFilesList.Add(failedFile);
                }
            });

            return failedFilesList.ToList();
        }

        public async Task<(int, List<FailursReason>)> NumberOfFaileds(string path)
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
