using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FileExporter.Models;
using FileExporter.Services;
using FileExporter.Interface;

namespace FileExporter.tests
{
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
                DepthGroupDNnames = new List<string> { "test-dname" },
                ProgressLogThreshold = 10
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
        public async Task SearchFolderForFailuresAsync_WithStreamingLogic_ShouldAggregateAndRecordCorrectMetrics()
        {
            // ARRANGE
            var rootDir = Path.Combine("base", "test");
            var scanPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "my-dname-landing-dir-prod");
            var dName = "test-dname";
            var env = "prod";
            var expectedNormalizedDName = "Test-dname";

            // *** THE FIX: Ensure the target directory for file writing exists before the test runs ***
            Directory.CreateDirectory(scanPath);

            try
            {
                var group1Path = Path.Combine(scanPath, "Group1");
                var failure1Path = Path.Combine(group1Path, "Failure1");
                var failure2Path = Path.Combine(group1Path, "Failure2");

                var group2Path = Path.Combine(scanPath, "Group2");
                var failure3Path = Path.Combine(group2Path, "Failure3");

                _fileHelperMock.Setup(h => h.GetSubDirectories(scanPath)).ReturnsAsync(new[] { "Group1", "Group2" });
                _fileHelperMock.Setup(h => h.GetSubDirectories(group1Path)).ReturnsAsync(new[] { "Failure1", "Failure2" });
                _fileHelperMock.Setup(h => h.GetSubDirectories(group2Path)).ReturnsAsync(new[] { "Failure3" });

                // Fixed call to It.Is
                _fileHelperMock.Setup(h => h.GetSubDirectories(It.Is<string>(s => s.Contains("Failure")))).ReturnsAsync(Enumerable.Empty<string>());

                _fileHelperMock.Setup(h => h.GetSingleFailureReasonAsync(failure1Path)).ReturnsAsync(new FailureReason { Path = failure1Path, Reason = "Recent fail 1", LastWriteTime = DateTime.UtcNow.AddHours(-1) });
                _fileHelperMock.Setup(h => h.GetSingleFailureReasonAsync(failure2Path)).ReturnsAsync(new FailureReason { Path = failure2Path, Reason = "Old fail", LastWriteTime = DateTime.UtcNow.AddHours(-48) });
                _fileHelperMock.Setup(h => h.GetSingleFailureReasonAsync(failure3Path)).ReturnsAsync(new FailureReason { Path = failure3Path, Reason = "Recent fail 2", LastWriteTime = DateTime.UtcNow.AddMinutes(-30) });

                _fileHelperMock.Setup(h => h.FindImageInDirectory(It.IsAny<string>())).Returns("path/to/image.jpg");

                // ACT
                await _service.SearchFolderForFailuresAsync(rootDir, scanPath, dName, env);

                // ASSERT
                _metricsManagerMock.Verify(m => m.SetGaugeValue("total_nFailures", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "false"), 3), Times.Once);
                _metricsManagerMock.Verify(m => m.SetGaugeValue("total_nFailures", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(vals => vals[1] == expectedNormalizedDName && vals[3] == "true"), 2), Times.Once);

                var expectedGroup1RelativePath = Path.GetRelativePath(rootDir, group1Path).Replace(Path.DirectorySeparatorChar, '/');
                _metricsManagerMock.Verify(m => m.SetGaugeValue("n_failures_in_group_folder", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(vals => vals[3] == expectedGroup1RelativePath && vals[4] == "false"), 2), Times.Once);
                _metricsManagerMock.Verify(m => m.SetGaugeValue("n_failures_in_group_folder", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(vals => vals[3] == expectedGroup1RelativePath && vals[4] == "true"), 1), Times.Once);

                var expectedGroup2RelativePath = Path.GetRelativePath(rootDir, group2Path).Replace(Path.DirectorySeparatorChar, '/');
                _metricsManagerMock.Verify(m => m.SetGaugeValue("n_failures_in_group_folder", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(vals => vals[3] == expectedGroup2RelativePath && vals[4] == "false"), 1), Times.Once);
                _metricsManagerMock.Verify(m => m.SetGaugeValue("n_failures_in_group_folder", It.IsAny<string>(), It.IsAny<string[]>(), It.Is<string[]>(vals => vals[3] == expectedGroup2RelativePath && vals[4] == "true"), 1), Times.Once);
            }
            finally
            {
                // Clean up the temporary directory
                Directory.Delete(Path.GetDirectoryName(scanPath)!, recursive: true);
            }
        }
    }
}
