using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FileExporter.Models;
using FileExporter.Services;
using FileExporter.Interface;
using System.Text.Json;

namespace FileExporter.tests
{
    public class FailureSearchServiceTests
    {
        private readonly Mock<IOptions<Settings>> _settingsMock;
        private readonly Mock<ILogger<FailureSearchService>> _loggerMock;
        private readonly Mock<IMetricsManager> _metricsManagerMock;
        private readonly Mock<IFileHelper> _fileHelperMock;
        private readonly Mock<ITraversalService> _traversalServiceMock;
        private readonly FailureSearchService _service;

        public FailureSearchServiceTests()
        {
            _settingsMock = new Mock<IOptions<Settings>>();
            _loggerMock = new Mock<ILogger<FailureSearchService>>();
            _metricsManagerMock = new Mock<IMetricsManager>();
            _fileHelperMock = new Mock<IFileHelper>();
            _traversalServiceMock = new Mock<ITraversalService>();

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
                _fileHelperMock.Object,
                _traversalServiceMock.Object
            );
        }

        [Fact]
        public async Task SearchFolderForFailuresAsync_ShouldAggregateAndRecordCorrectMetrics()
        {
            // ARRANGE
            var rootDir = Path.Combine("base", "test");
            var scanPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "my-dname-landing-dir-prod");
            var dName = "test-dname";
            var env = "prod";
            var expectedNormalizedDName = "Test-dname";

            Directory.CreateDirectory(scanPath);

            try
            {
                var group1Path = Path.Combine(scanPath, "Group1");
                var failure1Path = Path.Combine(group1Path, "Failure1"); // Recent
                var failure2Path = Path.Combine(group1Path, "Failure2"); // Old

                var group2Path = Path.Combine(scanPath, "Group2");
                var failure3Path = Path.Combine(group2Path, "Failure3"); // Recent

                _fileHelperMock.Setup(h => h.GetSingleFailureReasonAsync(failure1Path)).ReturnsAsync(new FailureReason { Path = failure1Path, Reason = "Recent fail 1", LastWriteTime = DateTime.UtcNow.AddHours(-1) });
                _fileHelperMock.Setup(h => h.GetSingleFailureReasonAsync(failure2Path)).ReturnsAsync(new FailureReason { Path = failure2Path, Reason = "Old fail", LastWriteTime = DateTime.UtcNow.AddHours(-48) });
                _fileHelperMock.Setup(h => h.GetSingleFailureReasonAsync(failure3Path)).ReturnsAsync(new FailureReason { Path = failure3Path, Reason = "Recent fail 2", LastWriteTime = DateTime.UtcNow.AddMinutes(-30) });
                _fileHelperMock.Setup(h => h.FindImageInDirectory(It.IsAny<string>())).Returns("path/to/image.jpg");

                // --- START OF CHANGE: This is the correct way to mock it ---
                // 1. Create a single report object that will be populated and returned.
                var populatedReport = new ScanReport();

                _traversalServiceMock.Setup(t => t.TraverseAndAggregateAsync(scanPath, expectedNormalizedDName, It.IsAny<Func<string, List<string>, ScanReport, Task>>()))
                    .Callback<string, string, Func<string, List<string>, ScanReport, Task>>(async (path, d, processFunc) =>
                    {
                        // 2. The callback now populates the single report object.
                        await processFunc(failure1Path, new List<string> { group1Path }, populatedReport);
                        await processFunc(failure2Path, new List<string> { group1Path }, populatedReport);
                        await processFunc(failure3Path, new List<string> { group2Path }, populatedReport);
                    })
                    // 3. ReturnsAsync returns the *same* populated object.
                    .ReturnsAsync(populatedReport);
                // --- END OF CHANGE ---

                // ACT
                await _service.SearchFolderForFailuresAsync(rootDir, scanPath, dName, env);

                // ASSERT
                // Now the assertions should pass because the report object contains all the data.
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
                if (Directory.Exists(scanPath))
                {
                    Directory.Delete(scanPath, recursive: true);
                }
            }
        }
    }
}