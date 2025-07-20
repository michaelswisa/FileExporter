namespace FileExporterNew.Models
{
    public class Settings
    {
        public string RootPath { get; set; } = string.Empty;
        public string Env { get; set; } = string.Empty;
        public int MaxFailures { get; set; }
        public int MaxFilesToRead { get; set; }
        public int MaxDepth { get; set; }
        public int RecentTimeWindowHours { get; set; }
        public List<string> GroupedDNnames { get; set; } = new();
        public int ZombieTimeThresholdMinutes { get; set; }
        public int ScanIntervalMinutes { get; set; }
    }
}