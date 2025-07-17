namespace FileExporterNew.Models
{
    public class ZombieInfo
    {
        public string Path { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
        public TimeSpan TimeSinceCreation { get; set; }
        public string ZombieType { get; set; } = string.Empty;
    }
}
