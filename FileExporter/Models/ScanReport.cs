namespace FileExporter.Models
{
    public class ScanReport
    {
        public int TotalItemsFound { get; set; }
        public int RecentItemsFound { get; set; }
        public Dictionary<string, int> GroupFolderCountsAll { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> GroupFolderCountsRecent { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
