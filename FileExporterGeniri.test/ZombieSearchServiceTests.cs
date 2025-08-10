using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FileExporterNew.Models;
using FileExporterNew.Services;

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
            ZombieTimeThresholdMinutes = 60,
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
            _fileHelperMock.Object
        );
    }

    [Fact]
    public async Task ShouldDetect_ObservedZombie_WhenOldEnough()
    {
        // Arrange
        var dName = "default-dname";
        var zombieFolderPath = "C:\\test\\zombie-folder";
        var observedFileName = "file.observed";
        var fullObservedPath = Path.Combine(zombieFolderPath, observedFileName);

        // This file is older than the threshold, but newer than the "RecentTimeWindowHours"
        var oldFileWriteTime = DateTime.Now.AddMinutes(-_settings.ZombieTimeThresholdMinutes - 5);

        _fileHelperMock.Setup(h => h.GetSubDirectories("C:\\test")).ReturnsAsync(new[] { "zombie-folder" });
        _fileHelperMock.Setup(h => h.GetSubDirectories(zombieFolderPath)).ReturnsAsync(Array.Empty<string>());
        _fileHelperMock.Setup(h => h.IsInObservedNotFailed(zombieFolderPath)).ReturnsAsync(true);
        _fileHelperMock.Setup(h => h.GetFileNameContaining(zombieFolderPath, "observed")).ReturnsAsync(observedFileName);
        _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(fullObservedPath)).ReturnsAsync(oldFileWriteTime);

        // Act
        await _service.SearchFolderForObservedZombiesAsync("C:\\test", "C:\\test", dName, "prod");

        // Assert
        var expectedNormalizedDName = "Default-dname";

        // It was found, so the "all items" count is 1
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
            "total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(),
            It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "false" && vals[4] == "observed"), 1), Times.Once);

        // The item is recent (within 24 hours), so the "recent items" count is also 1
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
            "total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(),
            It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "true" && vals[4] == "observed"), 1), Times.Once);
    }

    [Fact]
    public async Task ShouldNotDetect_ObservedZombie_WhenTooRecent()
    {
        // Arrange
        var dName = "default-dname";
        var zombieFolderPath = "C:\\test\\zombie-folder";
        var observedFileName = "file.observed";
        var fullObservedPath = Path.Combine(zombieFolderPath, observedFileName);

        // This file is younger than the threshold
        var recentFileWriteTime = DateTime.Now.AddMinutes(-_settings.ZombieTimeThresholdMinutes + 5);

        _fileHelperMock.Setup(h => h.GetSubDirectories("C:\\test")).ReturnsAsync(new[] { "zombie-folder" });
        _fileHelperMock.Setup(h => h.GetSubDirectories(zombieFolderPath)).ReturnsAsync(Array.Empty<string>());
        _fileHelperMock.Setup(h => h.IsInObservedNotFailed(zombieFolderPath)).ReturnsAsync(true);
        _fileHelperMock.Setup(h => h.GetFileNameContaining(zombieFolderPath, "observed")).ReturnsAsync(observedFileName);
        _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(fullObservedPath)).ReturnsAsync(recentFileWriteTime);

        // Act
        await _service.SearchFolderForObservedZombiesAsync("C:\\test", "C:\\test", dName, "prod");

        // Assert
        var expectedNormalizedDName = "Default-dname";

        // It was not found, so "all items" count is 0
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
            "total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(),
            It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "false" && vals[4] == "observed"), 0), Times.Once);

        // It was not found, so "recent items" count is 0
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
            "total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(),
            It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "true" && vals[4] == "observed"), 0), Times.Once);
    }

    [Fact]
    public async Task ShouldDetect_NonObservedZombie_WhenOldEnough()
    {
        // Arrange
        var dName = "default-dname";
        var zombieFolderPath = "C:\\test\\zombie-folder";
        var oldDirectoryWriteTime = DateTime.Now.AddMinutes(-_settings.ZombieTimeThresholdMinutes - 5);

        _fileHelperMock.Setup(h => h.GetSubDirectories("C:\\test")).ReturnsAsync(new[] { "zombie-folder" });
        _fileHelperMock.Setup(h => h.GetSubDirectories(zombieFolderPath)).ReturnsAsync(Array.Empty<string>());
        _fileHelperMock.Setup(h => h.NotObservedAndNotFailed(zombieFolderPath)).ReturnsAsync(true);
        _fileHelperMock.Setup(h => h.GetDirectoryLastWriteTimeAsync(zombieFolderPath)).ReturnsAsync(oldDirectoryWriteTime);

        // Act
        await _service.SearchFolderForNonObservedZombiesAsync("C:\\test", "C:\\test", dName, "prod");

        // Assert
        var expectedNormalizedDName = "Default-dname";

        // It was found, so "all items" count is 1
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
            "total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(),
            It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "false" && vals[4] == "non_observed"), 1), Times.Once);

        // The item is recent (within 24 hours), so "recent items" count is 1
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
            "total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(),
            It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "true" && vals[4] == "non_observed"), 1), Times.Once);
    }

    [Fact]
    public async Task ShouldUseDNameSpecificThreshold_AndDetectZombie()
    {
        // Arrange
        var dName = "special-dname";
        var zombieFolderPath = "C:\\test\\zombie-folder";
        var observedFileName = "file.observed";
        var fullObservedPath = Path.Combine(zombieFolderPath, observedFileName);

        var oldFileWriteTime = DateTime.Now.AddMinutes(-_settings.ZombieThresholdsByDName[dName] - 5);

        _fileHelperMock.Setup(h => h.GetSubDirectories("C:\\test")).ReturnsAsync(new[] { "zombie-folder" });
        _fileHelperMock.Setup(h => h.GetSubDirectories(zombieFolderPath)).ReturnsAsync(Array.Empty<string>());
        _fileHelperMock.Setup(h => h.IsInObservedNotFailed(zombieFolderPath)).ReturnsAsync(true);
        _fileHelperMock.Setup(h => h.GetFileNameContaining(zombieFolderPath, "observed")).ReturnsAsync(observedFileName);
        _fileHelperMock.Setup(h => h.GetFileLastWriteTimeAsync(fullObservedPath)).ReturnsAsync(oldFileWriteTime);

        // Act
        await _service.SearchFolderForObservedZombiesAsync("C:\\test", "C:\\test", dName, "prod");

        // Assert
        var expectedNormalizedDName = "Special-dname";

        // It was found, so "all items" count is 1
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
            "total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(),
            It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "false" && vals[4] == "observed"), 1), Times.Once);

        // The item is recent (within 24 hours), so "recent items" count is 1
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
            "total_n_zombies", It.IsAny<string>(), It.IsAny<string[]>(),
            It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "true" && vals[4] == "observed"), 1), Times.Once);
    }
}