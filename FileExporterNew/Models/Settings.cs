namespace FileExporterNew.Models
{
    public class Settings
    {
        public string RootPath { get; set; } = string.Empty;
        public string Env { get; set; } = string.Empty;
        public int MaxFailures { get; set; }
        public int MaxFilesToRead { get; set; }
        public int MaxDepth { get; set; }
        public int RecentErrorsTimeWindowHours { get; set; }
        public int CleanupTimerIntervalHours { get; set; }
        public List<string> GroupedDNnames { get; set; } = new();
        public int MaxZombies { get; set; }
        public int ObservedZombieThresholdMinutes { get; set; }
        public int NonObservedZombieThresholdMinutes { get; set; }
        public int RecentZombiesTimeWindowHours { get; set; }
    }
}
