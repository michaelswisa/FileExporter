namespace FileExporter.Models
{
    public class Settings
    {
        public string RootPath { get; set; } = string.Empty;
        public string Env { get; set; } = string.Empty;
        public int MaxFailures { get; set; }
        public int MaxDepth { get; set; }
        public int RecentTimeWindowHours { get; set; }
        public int MaxParallelDNameScans { get; set; }
        public List<string> DepthGroupDNnames { get; set; } = new();
        public List<string> SupportedImageExtensions { get; set; } = new();
        public Dictionary<string, int> ZombieThresholdsByDName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int ZombieTimeThresholdMinutes { get; set; }
        public int ScanIntervalMinutes { get; set; }
        public int ProgressLogThreshold { get; set; }
    }
}
