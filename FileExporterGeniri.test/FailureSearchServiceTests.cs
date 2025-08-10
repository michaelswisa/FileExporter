using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FileExporterNew.Models;
using FileExporterNew.Services;

public class FailureSearchServiceTests
{
    private readonly Mock<IOptions<Settings>> _settingsMock;
    private readonly Mock<ILogger<FailureSearchService>> _loggerMock;
    private readonly Mock<IMetricsManager> _metricsManagerMock;
    private readonly Mock<IFileHelper> _fileHelperMock;
    private readonly FailureSearchService _service;

    public FailureSearchServiceTests()
    {
        _settingsMock = new Mock<IOptions<Settings>>();
        _loggerMock = new Mock<ILogger<FailureSearchService>>();
        _metricsManagerMock = new Mock<IMetricsManager>();
        _fileHelperMock = new Mock<IFileHelper>();

        var settings = new Settings
        {
            MaxFailures = 100,
            MaxDepth = 3,
            RecentTimeWindowHours = 24,
            GroupedDNnames = new List<string> { "test-dname" }
        };
        _settingsMock.Setup(s => s.Value).Returns(settings);

        _service = new FailureSearchService(
            _settingsMock.Object,
            _loggerMock.Object,
            _metricsManagerMock.Object,
            _fileHelperMock.Object
        );
    }

    [Fact]
    public async Task SearchFolderForFailuresAsync_WhenOneFailureExists_ShouldFindItAndRecordMetrics()
    {
        // Arrange
        var rootDir = "C:\\test";
        var dName = "test-dname";
        var env = "prod";
        var failedFolderPath = "C:\\test\\folderA\\subfolder1";
        var expectedNormalizedDName = "Test-dname";
        var expectedImagePath = "C:\\path\\to\\image.jpg";

        _fileHelperMock.Setup(h => h.GetSubDirectories("C:\\test")).ReturnsAsync(new[] { "folderA" });
        _fileHelperMock.Setup(h => h.GetSubDirectories("C:\\test\\folderA")).ReturnsAsync(new[] { "subfolder1", "subfolder2" });
        _fileHelperMock.Setup(h => h.GetSubDirectories(It.Is<string>(s => s.Contains("subfolder")))).ReturnsAsync(Array.Empty<string>());

        var failureReason = new FailureReason
        {
            Path = failedFolderPath,
            Reason = "Something went wrong",
            LastWriteTime = DateTime.UtcNow // This is recent
        };
        _fileHelperMock.Setup(h => h.GetSingleFailureReasonAsync(failedFolderPath)).ReturnsAsync(failureReason);
        _fileHelperMock.Setup(h => h.GetSingleFailureReasonAsync(It.Is<string>(p => p != failedFolderPath))).ReturnsAsync((FailureReason?)null);

        // Setup the image finding part, which is now called during the scan
        _fileHelperMock.Setup(h => h.FindImageInDirectory(failedFolderPath)).Returns(expectedImagePath);

        // Act
        await _service.SearchFolderForFailuresAsync(rootDir, rootDir, dName, env);

        // Assert

        // 1. Verify total failures metric (is_recent = false)
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
            "total_nFailures",
            It.IsAny<string>(),
            It.Is<string[]>(labels => labels.SequenceEqual(new[] { "root_dir", "d_name", "env", "is_recent" })),
            It.Is<string[]>(vals => vals[0] == rootDir && vals[1] == expectedNormalizedDName && vals[2] == env && vals[3] == "false"),
            1
        ), Times.Once);

        // 2. Verify recent failures metric (is_recent = true)
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
            "total_nFailures",
            It.IsAny<string>(),
            It.Is<string[]>(labels => labels.SequenceEqual(new[] { "root_dir", "d_name", "env", "is_recent" })),
            It.Is<string[]>(vals => vals[0] == rootDir && vals[1] == expectedNormalizedDName && vals[2] == env && vals[3] == "true"),
            1
        ), Times.Once);

        // 3. Verify grouped folder failures metric (is_recent = false)
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
           "n_failures_in_group_folder",
           It.IsAny<string>(),
           It.Is<string[]>(labels => labels.SequenceEqual(new[] { "root_dir", "d_name", "env", "group_folder", "is_recent" })),
           It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "folderA" && vals[4] == "false"),
           1
       ), Times.Once);

        // 4. Verify grouped folder failures metric (is_recent = true)
        _metricsManagerMock.Verify(m => m.SetGaugeValue(
           "n_failures_in_group_folder",
           It.IsAny<string>(),
           It.Is<string[]>(labels => labels.SequenceEqual(new[] { "root_dir", "d_name", "env", "group_folder", "is_recent" })),
           It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "folderA" && vals[4] == "true"),
           1
       ), Times.Once);

        // 5. Verify that FindImageInDirectory was called ONLY ONCE during the scan.
        _fileHelperMock.Verify(h => h.FindImageInDirectory(failedFolderPath), Times.Once());
    }
}