using FileExporter.Interface;

namespace FileExporter.Models
{
    public class FailureReason : ISearchResult
    {
        public string Path { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
        public string? Image { get; set; }
    }
}
