namespace FileExporterNew.Models
{
    public class Settings
    {
        public int MaxFilesToRead { get; set; }
        public string[] GroupedDNnames { get; set; } = Array.Empty<string>();
        public int RecentErrorsTimeWindowHours { get; set; }
        public int MaxFailures { get; set; }
        public int MaxDepth { get; set; }
        public int CleanupTimerIntervalHours { get; set; }
    }
}
