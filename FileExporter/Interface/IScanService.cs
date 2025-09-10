namespace FileExporter.Interface
{
    public interface IScanService
    {
        Task SearchFolderAsync(string rootDir, string path, string dName, string env, object? scanContext = null);
    }

    public interface IFailureSearchService : IScanService
    {
        Task SearchFolderForFailuresAsync(string rootDir, string path, string dName, string env);
    }

    public interface IZombieSearchService : IScanService
    {
        Task SearchFolderForObservedZombiesAsync(string rootDir, string path, string dName, string env);
        Task SearchFolderForNonObservedZombiesAsync(string rootDir, string path, string dName, string env);
    }

    public interface ITranscodedSearchService : IScanService
    {
        Task SearchFoldersForTranscodedAsync(string rootDir, string path, string dName, string env);
    }
}
