namespace FileExporterNew.Models
{
    public class FailureReason
    {
        public string Path { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
    }
}
