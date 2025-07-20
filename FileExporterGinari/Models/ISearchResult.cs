namespace FileExporterNew.Models
{
    /// <summary>
    /// Defines a common interface for items found by a search service.
    /// This allows the base search service to work with different types of results (e.g., Failures, Zombies)
    /// without needing to be generic.
    /// </summary>
    public interface ISearchResult
    {
        string Path { get; }
        DateTime LastWriteTime { get; }
    }
}