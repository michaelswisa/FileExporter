namespace FileExporterNew.Models
{
    public class FailureReason
    {
        public string Path { get; set; }
        public string Reason { get; set; }
        public string Image { get; set; }
        public DateTime LastWriteTime { get; set; }
    }
} 