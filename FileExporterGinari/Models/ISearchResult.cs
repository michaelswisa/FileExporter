namespace FileExporterNew.Models
{
    public interface ISearchResult
    {
        string Path { get; }
        DateTime LastWriteTime { get; }
    }
}