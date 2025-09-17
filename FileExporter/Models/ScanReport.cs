using System.Collections.Concurrent;

namespace FileExporter.Models
{
    public class ScanReport
    {
        private int _totalItemsFound;
        private int _recentItemsFound;

        public int TotalItemsFound => _totalItemsFound;
        public int RecentItemsFound => _recentItemsFound;

        public ConcurrentDictionary<string, int> GroupFolderCountsAll { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, int> GroupFolderCountsRecent { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void IncrementTotalItems() => Interlocked.Increment(ref _totalItemsFound);
        public void IncrementRecentItems() => Interlocked.Increment(ref _recentItemsFound);
    }
}
