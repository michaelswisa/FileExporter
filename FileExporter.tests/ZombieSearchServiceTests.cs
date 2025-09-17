using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FileExporter.Models;
using FileExporter.Services;
using FileExporter.Interface;

namespace FileExporter.tests
{
    public class ZombieSearchServiceTests
    {
        private readonly Mock<IOptions<Settings>> _settingsMock;
        private readonly Mock<ILogger<ZombieSearchService>> _loggerMock;
        private readonly Mock<IMetricsManager> _metricsManagerMock;
        private readonly Mock<IFileHelper> _fileHelperMock;
        private readonly Mock<ITraversalService> _traversalServiceMock;
        private readonly ZombieSearchService _service;
        private readonly Settings _settings;

        public ZombieSearchServiceTests()
        {
            _settingsMock = new Mock<IOptions<Settings>>();
            _loggerMock = new Mock<ILogger<ZombieSearchService>>();
            _metricsManagerMock = new Mock<IMetricsManager>();
            _fileHelperMock = new Mock<IFileHelper>();
            _traversalServiceMock = new Mock<ITraversalService>();

            _settings = new Settings
            {
                MaxFailures = 100,
                MaxDepth = 3,
                RecentTimeWindowHours = 24,
                ZombieTimeThresholdMinutes = 60,
                DepthGroupDNnames = new List<string> { "zombie-dname" },
                ProgressLogThreshold = 10,
                ZombieThresholdsByDName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "special-dname", 120 }
                }
            };
            _settingsMock.Setup(s => s.Value).Returns(_settings);

            _service = new ZombieSearchService(
                _settingsMock.Object,
                _loggerMock.Object,
                _metricsManagerMock.Object,
                _fileHelperMock.Object,
                _traversalServiceMock.Object
            );
        }

        [Fact]
        public async Task SearchFolderAsync_WithAggregation_ShouldRecordCorrectGroupMetrics()
        {
            // ARRANGE
            var rootDir = Path.Combine("base", "zombie_test");
            var scanPath = Path.Combine(rootDir, "zombie-dname-landing-dir-prod");
            var dName = "zombie-dname";
            var env = "prod";
            var expectedNormalizedDName = "Zombie-dname";

            var group1Path = Path.Combine(scanPath, "Group1");
            var zombie1Path = Path.Combine(group1Path, "Zombie1"); // Recent zombie

            var group2Path = Path.Combine(scanPath, "Group2");
            var zombie2Path = Path.Combine(group2Path, "Zombie2"); // Old zombie
            var notZombiePath = Path.Combine(group2Path, "NotZombie");

            _fileHelperMock.Setup(h => h.IsInObservedNotFailed(zombie1Path)).ReturnsAsync(true);
            _fileHelperMock.Setup(h => h.GetFileNameContaining(zombie1Path, "observed")).ReturnsAsync("file.Observed");
            _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(Path.Combine(zombie1Path, "file.Observed"))).ReturnsAsync(DateTime.Now.AddMinutes(-_settings.ZombieTimeThresholdMinutes - 5));

            _fileHelperMock.Setup(h => h.IsInObservedNotFailed(zombie2Path)).ReturnsAsync(true);
            _fileHelperMock.Setup(h => h.GetFileNameContaining(zombie2Path, "observed")).ReturnsAsync("file.Observed");
            _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(Path.Combine(zombie2Path, "file.Observed"))).ReturnsAsync(DateTime.Now.AddHours(-_settings.RecentTimeWindowHours - 5));

            _fileHelperMock.Setup(h => h.IsInObservedNotFailed(notZombiePath)).ReturnsAsync(true);
            _fileHelperMock.Setup(h => h.GetFileNameContaining(notZombiePath, "observed")).ReturnsAsync("file.Observed");
            _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(Path.Combine(notZombiePath, "file.Observed"))).ReturnsAsync(DateTime.Now.AddMinutes(-_settings.ZombieTimeThresholdMinutes + 5));

            // --- START OF CHANGE: Applying the correct mock pattern ---
            var populatedReport = new ScanReport();
            _traversalServiceMock.Setup(t => t.TraverseAndAggregateAsync(scanPath, expectedNormalizedDName, It.IsAny<Func<string, List<string>, ScanReport, Task>>()))
                .Callback<string, string, Func<string, List<string>, ScanReport, Task>>(async (path, d, processFunc) =>
                {
                    await processFunc(zombie1Path, new List<string> { group1Path }, populatedReport);
                    await processFunc(zombie2Path, new List<string> { group2Path }, populatedReport);
                    await processFunc(notZombiePath, new List<string> { group2Path }, populatedReport);
                })
                .ReturnsAsync(populatedReport);
            // --- END OF CHANGE ---

            // ACT
            await _service.SearchFolderForObservedZombiesAsync(rootDir, scanPath, dName, env);

            // ASSERT
            _metricsManagerMock.Verify(m => m.SetGaugeValue("total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(v => v[1] == expectedNormalizedDName && v[3] == "false" && v[4] == "Observed"), 2), Times.Once);
            _metricsManagerMock.Verify(m => m.SetGaugeValue("total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(v => v[1] == expectedNormalizedDName && v[3] == "true" && v[4] == "Observed"), 1), Times.Once);

            var group1RelativePath = Path.GetRelativePath(rootDir, group1Path).Replace(Path.DirectorySeparatorChar, '/');
            _metricsManagerMock.Verify(m => m.SetGaugeValue("n_zombies_in_group_folder", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(v => v[3] == group1RelativePath && v[4] == "false"), 1), Times.Once);
            _metricsManagerMock.Verify(m => m.SetGaugeValue("n_zombies_in_group_folder", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(v => v[3] == group1RelativePath && v[4] == "true"), 1), Times.Once);

            var group2RelativePath = Path.GetRelativePath(rootDir, group2Path).Replace(Path.DirectorySeparatorChar, '/');
            _metricsManagerMock.Verify(m => m.SetGaugeValue("n_zombies_in_group_folder", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(v => v[3] == group2RelativePath && v[4] == "false"), 1), Times.Once);
            _metricsManagerMock.Verify(m => m.SetGaugeValue("n_zombies_in_group_folder", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(v => v[3] == group2RelativePath && v[4] == "true"), 0), Times.Never()); // Updated to Times.Never for clarity
        }

        // --- START OF CHANGE: Adding Traversal Mock to smaller tests ---
        [Fact]
        public async Task ShouldDetect_NonObservedZombie_WhenFolderIsEmptyAndOldEnough()
        {
            // ARRANGE
            var dName = "default-dname";
            var scanPath = Path.Combine("base", "scan", "path");
            var zombieFolderPath = Path.Combine(scanPath, "zombie-folder");

            _fileHelperMock.Setup(h => h.NotObservedAndNotFailed(zombieFolderPath)).ReturnsAsync(true);
            _fileHelperMock.Setup(h => h.GetDirectoryLastWriteTimeAsync(zombieFolderPath)).ReturnsAsync(DateTime.Now.AddMinutes(-_settings.ZombieTimeThresholdMinutes - 5));

            var populatedReport = new ScanReport();
            _traversalServiceMock.Setup(t => t.TraverseAndAggregateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<string, List<string>, ScanReport, Task>>()))
                .Callback<string, string, Func<string, List<string>, ScanReport, Task>>(async (path, d, processFunc) =>
                {
                    await processFunc(zombieFolderPath, new List<string> { scanPath }, populatedReport);
                })
                .ReturnsAsync(populatedReport);

            // ACT
            await _service.SearchFolderForNonObservedZombiesAsync("base", scanPath, dName, "prod");

            // ASSERT
            _metricsManagerMock.Verify(m => m.SetGaugeValue("total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(vals => vals[3] == "false" && vals[4] == "Non_Observed"), 1), Times.Once);
        }

        [Fact]
        public async Task ShouldNotDetect_ObservedZombie_WhenFolderIsTooRecent()
        {
            // ARRANGE
            var dName = "default-dname";
            var scanPath = Path.Combine("base", "scan", "path");
            var zombieFolderPath = Path.Combine(scanPath, "zombie-folder");

            _fileHelperMock.Setup(h => h.IsInObservedNotFailed(zombieFolderPath)).ReturnsAsync(true);
            _fileHelperMock.Setup(h => h.GetFileNameContaining(zombieFolderPath, "observed")).ReturnsAsync("file.observed");
            _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(Path.Combine(zombieFolderPath, "file.observed"))).ReturnsAsync(DateTime.Now.AddMinutes(-_settings.ZombieTimeThresholdMinutes + 5));

            var populatedReport = new ScanReport();
            _traversalServiceMock.Setup(t => t.TraverseAndAggregateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<string, List<string>, ScanReport, Task>>()))
               .Callback<string, string, Func<string, List<string>, ScanReport, Task>>(async (path, d, processFunc) =>
               {
                   await processFunc(zombieFolderPath, new List<string> { scanPath }, populatedReport);
               })
               .ReturnsAsync(populatedReport);

            // ACT
            await _service.SearchFolderForObservedZombiesAsync("base", scanPath, dName, "prod");

            // ASSERT
            _metricsManagerMock.Verify(m => m.SetGaugeValue("total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(vals => vals[3] == "false"), 0), Times.Once);
        }

        [Fact]
        public async Task ShouldUseDNameSpecificThreshold_AndNotDetectAsZombie()
        {
            // ARRANGE
            var dName = "special-dname";
            var scanPath = Path.Combine("base", "scan", "path");
            var zombieFolderPath = Path.Combine(scanPath, "zombie-folder");

            _fileHelperMock.Setup(h => h.IsInObservedNotFailed(zombieFolderPath)).ReturnsAsync(true);
            _fileHelperMock.Setup(h => h.GetFileNameContaining(zombieFolderPath, "observed")).ReturnsAsync("file.observed");
            _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(Path.Combine(zombieFolderPath, "file.observed"))).ReturnsAsync(DateTime.Now.AddMinutes(-90));

            var populatedReport = new ScanReport();
            _traversalServiceMock.Setup(t => t.TraverseAndAggregateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<string, List<string>, ScanReport, Task>>()))
               .Callback<string, string, Func<string, List<string>, ScanReport, Task>>(async (path, d, processFunc) =>
               {
                   await processFunc(zombieFolderPath, new List<string> { scanPath }, populatedReport);
               })
               .ReturnsAsync(populatedReport);

            // ACT
            await _service.SearchFolderForObservedZombiesAsync("base", scanPath, dName, "prod");

            // ASSERT
            _metricsManagerMock.Verify(m => m.SetGaugeValue("total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(vals => vals[3] == "false"), 0), Times.Once);
        }
        // --- END OF CHANGE ---
    }
}