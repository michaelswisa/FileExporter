namespace FileExporterNew.Models
{
    /// <summary>
    /// Represents a failure reason read from a .fail file.
    /// Implements ISearchResult to be compatible with the base search service.
    /// </summary>
    public class FailureReason : ISearchResult
    {
        public string Path { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
    }
}