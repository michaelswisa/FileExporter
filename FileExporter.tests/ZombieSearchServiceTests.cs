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
        private readonly ZombieSearchService _service;
        private readonly Settings _settings;

        public ZombieSearchServiceTests()
        {
            _settingsMock = new Mock<IOptions<Settings>>();
            _loggerMock = new Mock<ILogger<ZombieSearchService>>();
            _metricsManagerMock = new Mock<IMetricsManager>();
            _fileHelperMock = new Mock<IFileHelper>();

            _settings = new Settings
            {
                MaxFailures = 100,
                MaxDepth = 3,
                RecentTimeWindowHours = 24,
                ZombieTimeThresholdMinutes = 60, // Default threshold is 60 minutes
                DepthGroupDNnames = new List<string> { "zombie-dname" },
                ProgressLogThreshold = 10,
                ZombieThresholdsByDName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "special-dname", 120 } // Specific threshold is 120 minutes
            }
            };
            _settingsMock.Setup(s => s.Value).Returns(_settings);

            _service = new ZombieSearchService(
                _settingsMock.Object,
                _loggerMock.Object,
                _metricsManagerMock.Object,
                _fileHelperMock.Object
            );
        }

        // This is the main integration-style test we already have. It's good and should stay.
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
            var zombie1Path = Path.Combine(group1Path, "Zombie1");

            var group2Path = Path.Combine(scanPath, "Group2");
            var zombie2Path = Path.Combine(group2Path, "Zombie2");
            var notZombiePath = Path.Combine(group2Path, "NotZombie");

            _fileHelperMock.Setup(h => h.GetSubDirectories(scanPath)).ReturnsAsync(new[] { "Group1", "Group2" });
            _fileHelperMock.Setup(h => h.GetSubDirectories(group1Path)).ReturnsAsync(new[] { "Zombie1" });
            _fileHelperMock.Setup(h => h.GetSubDirectories(group2Path)).ReturnsAsync(new[] { "Zombie2", "NotZombie" });
            _fileHelperMock.Setup(h => h.GetSubDirectories(It.Is<string>(s => s.Contains("Zombie") || s.Contains("NotZombie")))).ReturnsAsync(Enumerable.Empty<string>());

            _fileHelperMock.Setup(h => h.IsInObservedNotFailed(zombie1Path)).ReturnsAsync(true);
            _fileHelperMock.Setup(h => h.GetFileNameContaining(zombie1Path, "observed")).ReturnsAsync("file.Observed");
            _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(Path.Combine(zombie1Path, "file.Observed"))).ReturnsAsync(DateTime.Now.AddMinutes(-_settings.ZombieTimeThresholdMinutes - 5));

            _fileHelperMock.Setup(h => h.IsInObservedNotFailed(zombie2Path)).ReturnsAsync(true);
            _fileHelperMock.Setup(h => h.GetFileNameContaining(zombie2Path, "observed")).ReturnsAsync("file.Observed");
            _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(Path.Combine(zombie2Path, "file.Observed"))).ReturnsAsync(DateTime.Now.AddHours(-_settings.RecentTimeWindowHours - 5));

            _fileHelperMock.Setup(h => h.IsInObservedNotFailed(notZombiePath)).ReturnsAsync(true);
            _fileHelperMock.Setup(h => h.GetFileNameContaining(notZombiePath, "observed")).ReturnsAsync("file.Observed");
            _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(Path.Combine(notZombiePath, "file.Observed"))).ReturnsAsync(DateTime.Now.AddMinutes(-_settings.ZombieTimeThresholdMinutes + 5));

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
        }

        [Fact]
        public async Task ShouldDetect_NonObservedZombie_WhenFolderIsEmptyAndOldEnough()
        {
            // ARRANGE
            var dName = "default-dname";
            var scanPath = Path.Combine("base", "scan", "path");
            var zombieFolderPath = Path.Combine(scanPath, "zombie-folder");

            // This directory is old enough to be a zombie
            var oldDirectoryWriteTime = DateTime.Now.AddMinutes(-_settings.ZombieTimeThresholdMinutes - 5);

            _fileHelperMock.Setup(h => h.GetSubDirectories(scanPath)).ReturnsAsync(new[] { "zombie-folder" });
            _fileHelperMock.Setup(h => h.GetSubDirectories(zombieFolderPath)).ReturnsAsync(Enumerable.Empty<string>()); // It has no subdirectories
            _fileHelperMock.Setup(h => h.NotObservedAndNotFailed(zombieFolderPath)).ReturnsAsync(true); // No 'observed' or 'fail' files
            _fileHelperMock.Setup(h => h.GetDirectoryLastWriteTimeAsync(zombieFolderPath)).ReturnsAsync(oldDirectoryWriteTime);

            // ACT
            await _service.SearchFolderForNonObservedZombiesAsync("base", scanPath, dName, "prod");

            // ASSERT
            // Verify that ONE zombie was reported.
            _metricsManagerMock.Verify(m => m.SetGaugeValue(
                "total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(),
                It.Is<string[]>(vals => vals[3] == "false" && vals[4] == "Non_Observed"), 1), Times.Once);
        }

        [Fact]
        public async Task ShouldNotDetect_ObservedZombie_WhenFolderIsTooRecent()
        {
            // ARRANGE
            var dName = "default-dname";
            var scanPath = Path.Combine("base", "scan", "path");
            var zombieFolderPath = Path.Combine(scanPath, "zombie-folder");

            // This folder is younger than the threshold, so it's NOT a zombie
            var recentFileWriteTime = DateTime.Now.AddMinutes(-_settings.ZombieTimeThresholdMinutes + 5);

            _fileHelperMock.Setup(h => h.GetSubDirectories(scanPath)).ReturnsAsync(new[] { "zombie-folder" });
            _fileHelperMock.Setup(h => h.GetSubDirectories(zombieFolderPath)).ReturnsAsync(Enumerable.Empty<string>());
            _fileHelperMock.Setup(h => h.IsInObservedNotFailed(zombieFolderPath)).ReturnsAsync(true);
            _fileHelperMock.Setup(h => h.GetFileNameContaining(zombieFolderPath, "observed")).ReturnsAsync("file.observed");
            _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(Path.Combine(zombieFolderPath, "file.observed"))).ReturnsAsync(recentFileWriteTime);

            // ACT
            await _service.SearchFolderForObservedZombiesAsync("base", scanPath, dName, "prod");

            // ASSERT
            // Verify that NO zombies were reported because the folder was too new.
            _metricsManagerMock.Verify(m => m.SetGaugeValue(
                "total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(),
                It.Is<string[]>(vals => vals[3] == "false"), 0), Times.Once);
        }

        [Fact]
        public async Task ShouldUseDNameSpecificThreshold_AndNotDetectAsZombie()
        {
            // ARRANGE
            var dName = "special-dname"; // This dName has a 120-minute threshold
            var scanPath = Path.Combine("base", "scan", "path");
            var zombieFolderPath = Path.Combine(scanPath, "zombie-folder");

            // This folder's age is 90 minutes. It's older than the default (60) but YOUNGER than the specific threshold (120).
            // Therefore, it should NOT be considered a zombie for this dName.
            var trickyFileWriteTime = DateTime.Now.AddMinutes(-90);

            _fileHelperMock.Setup(h => h.GetSubDirectories(scanPath)).ReturnsAsync(new[] { "zombie-folder" });
            _fileHelperMock.Setup(h => h.GetSubDirectories(zombieFolderPath)).ReturnsAsync(Enumerable.Empty<string>());
            _fileHelperMock.Setup(h => h.IsInObservedNotFailed(zombieFolderPath)).ReturnsAsync(true);
            _fileHelperMock.Setup(h => h.GetFileNameContaining(zombieFolderPath, "observed")).ReturnsAsync("file.observed");
            _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(Path.Combine(zombieFolderPath, "file.observed"))).ReturnsAsync(trickyFileWriteTime);

            // ACT
            await _service.SearchFolderForObservedZombiesAsync("base", scanPath, dName, "prod");

            // ASSERT
            // Verify that NO zombie was reported because it didn't meet the specific 120-minute threshold.
            _metricsManagerMock.Verify(m => m.SetGaugeValue(
               "total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(),
               It.Is<string[]>(vals => vals[3] == "false"), 0), Times.Once);
        }
    }
}