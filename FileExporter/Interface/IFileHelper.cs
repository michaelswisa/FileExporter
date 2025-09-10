using FileExporter.Models;

namespace FileExporter.Interface
{
    public interface IFileHelper
    {
        Task<IEnumerable<string>> GetFilesInPath(string path);
        Task<IEnumerable<string>> GetSubDirectories(string path);
        Task<FailureReason?> ReadFileAsync(string filePath);
        Task<FailureReason?> GetSingleFailureReasonAsync(string path);
        string? FindImageInDirectory(string directoryPath);
        Task<string> GetFileNameContaining(string path, string substring);
        Task<bool> IsInObservedNotFailed(string path);
        Task<DateTime?> GetFileLastWriteTimeAsync(string filePath);
        Task<DateTime?> GetDirectoryLastWriteTimeAsync(string directoryPath);
        Task<bool> NotObservedAndNotFailed(string path);
    }
}
