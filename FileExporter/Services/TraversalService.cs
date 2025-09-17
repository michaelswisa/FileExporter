using FileExporter.Interface;
using FileExporter.Models;
using Microsoft.Extensions.Options;

namespace FileExporter.Services
{
    public class TraversalService : ITraversalService
    {
        private readonly Settings _settings;
        private readonly ILogger<TraversalService> _logger;
        private readonly IFileHelper _fileHelper;

        public TraversalService(IOptions<Settings> settings, ILogger<TraversalService> logger, IFileHelper fileHelper)
        {
            _settings = settings.Value;
            _logger = logger;
            _fileHelper = fileHelper;
        }

        public async Task<ScanReport> TraverseAndAggregateAsync(
            string rootPath,
            string dName,
            Func<string, List<string>, ScanReport, Task> processPathAsync)
        {
            var report = new ScanReport();
            int maxConcurrency = _settings.MaxConcurrentDirectoryScans;
            using var semaphore = new SemaphoreSlim(maxConcurrency);

            var stack = new Stack<(string path, int depth, List<string> parentGroups)>();
            stack.Push((rootPath, 0, new List<string>()));

            var isGroupedDName = _settings.DepthGroupDNnames.Any(name => name.Equals(dName, StringComparison.OrdinalIgnoreCase));
            var maxDepth = isGroupedDName ? _settings.MaxDepth : 1;

            while (stack.Count > 0)
            {
                if (report.TotalItemsFound > _settings.MaxFailures)
                {
                    _logger.LogInformation($"Reached MaxFailures limit of {_settings.MaxFailures}. Stopping traversal for {dName}.");
                    break;
                }

                var (currentPath, depth, parentGroups) = stack.Pop();
                try
                {
                    if (depth > 0)
                    {
                        await semaphore.WaitAsync();
                        _ = processPathAsync(currentPath, parentGroups, report)
                            .ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                {
                                    _logger.LogError(t.Exception, "Error processing path in parallel: {CurrentPath}", currentPath);
                                }
                                semaphore.Release();
                            });
                    }

                    if (depth < maxDepth)
                    {
                        var subdirs = await _fileHelper.GetSubDirectories(currentPath);
                        foreach (var subdir in subdirs.Reverse())
                        {
                            var nextParentGroups = new List<string>(parentGroups);
                            if (depth == 0)
                            {
                                nextParentGroups.Add(Path.Combine(currentPath, subdir));
                            }
                            stack.Push((Path.Combine(currentPath, subdir), depth + 1, nextParentGroups));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during directory traversal setup: {CurrentPath}", currentPath);
                }
            }

            for (int i = 0; i < maxConcurrency; i++)
            {
                await semaphore.WaitAsync();
            }

            return report;
        }
    }
}
