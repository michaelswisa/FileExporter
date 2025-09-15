namespace FileExporter.Models
{
    public class ScanAllResult
    {
        public bool FailureScanQueued { get; set; }
        public bool ObservedZombieScanQueued { get; set; }
        public bool NonObservedZombieScanQueued { get; set; }
        public bool TranscodedScanQueued { get; set; }
        public bool AnyScanQueued => FailureScanQueued || ObservedZombieScanQueued || NonObservedZombieScanQueued || TranscodedScanQueued;
        public List<string> Messages { get; } = new List<string>();
    }
}
