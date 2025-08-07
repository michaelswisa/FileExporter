namespace FileExporterNew.Models
{
    public class DirectoryScanReport
    {
        public List<ISearchResult> FoundItems { get; set; } = new();
        public Dictionary<string, int> GroupFolderCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}