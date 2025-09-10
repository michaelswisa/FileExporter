namespace FileExporter.Interface
{
    public interface ISearchResult
    {
        string Path { get; }
        DateTime LastWriteTime { get; }
    }
}
