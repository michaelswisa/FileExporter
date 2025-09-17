using FileExporter.Models;

namespace FileExporter.Interface
{
    public interface ITraversalService
    {
        Task<ScanReport> TraverseAndAggregateAsync(
            string rootPath,
            string dName,
            Func<string, List<string>, ScanReport, Task> processPathAsync);
    }
}
