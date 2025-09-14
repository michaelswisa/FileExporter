using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FileExporter.Models;
using FileExporter.Services;
using FileExporter.Interface;

namespace FileExporter.tests
{
    public class ScanManagerServiceLogicTests
    {
        private readonly Mock<ILogger<ScanManagerService>> _loggerMock;
        private readonly Mock<IOptions<Settings>> _settingsMock;
        private readonly Mock<IFailureSearchService> _failureSearcherMock;
        private readonly Mock<IZombieSearchService> _zombieSearcherMock;
        private readonly Mock<ITranscodedSearchService> _transcodedSearcherMock;
        private readonly Mock<IFileHelper> _fileHelperMock;

        public ScanManagerServiceLogicTests()
        {
            _loggerMock = new Mock<ILogger<ScanManagerService>>();
            _settingsMock = new Mock<IOptions<Settings>>();
            _failureSearcherMock = new Mock<IFailureSearchService>();
            _zombieSearcherMock = new Mock<IZombieSearchService>();
            _transcodedSearcherMock = new Mock<ITranscodedSearchService>();
            _fileHelperMock = new Mock<IFileHelper>();
        }

        [Fact]
        public async Task DiscoverAndScanAllAsync_ShouldOnlyTriggerScansForValidDirectoriesInCorrectEnv()
        {
            // ARRANGE

            // 1. Setup settings
            var settings = new Settings
            {
                RootPath = Path.Combine(Directory.GetCurrentDirectory(), "root"),
                Env = "prod",
                MaxParallelDNameScans = 4
            };
            _settingsMock.Setup(s => s.Value).Returns(settings);

            // 2. Mock file system to provide a list of directories
            var subDirectories = new[]
            {
            "service-a-landing-dir-prod",   // Valid, correct env
            "service-b-landing-dir-prod",   // Valid, correct env
            "service-c-landing-dir-dev",    // Valid, wrong env
            "invalid-directory-name",       // Invalid name
            "service-d-transcoded"          // Does not match discovery pattern
        };
            _fileHelperMock.Setup(h => h.GetSubDirectories(settings.RootPath)).ReturnsAsync(subDirectories);

            // 3. Create a "Spy" of the ScanManagerService.
            // This allows us to execute the real `DiscoverAndScanAllAsync` method,
            // while verifying calls to its own (virtual) methods.
            var scanManagerSpy = new Mock<ScanManagerService>(
                _loggerMock.Object,
                _settingsMock.Object,
                _failureSearcherMock.Object,
                _zombieSearcherMock.Object,
                _transcodedSearcherMock.Object,
                _fileHelperMock.Object
            )
            { CallBase = true }; // `CallBase = true` is crucial for a spy.

            // We setup the method we want to track. It will now return a completed task instead of running its real logic.
            scanManagerSpy.Setup(s => s.ScanAllTypesForDNameAsync(It.IsAny<string>())).ReturnsAsync(true);

            // ACT
            await scanManagerSpy.Object.DiscoverAndScanAllAsync();

            // ASSERT

            // Verify that the main scanning method was called ONLY for the valid dNames.
            scanManagerSpy.Verify(s => s.ScanAllTypesForDNameAsync("service-a"), Times.Once());
            scanManagerSpy.Verify(s => s.ScanAllTypesForDNameAsync("service-b"), Times.Once());

            // Verify it was NOT called for irrelevant or invalid directories.
            scanManagerSpy.Verify(s => s.ScanAllTypesForDNameAsync("service-c"), Times.Never());
            scanManagerSpy.Verify(s => s.ScanAllTypesForDNameAsync(It.Is<string>(d => d.Contains("invalid"))), Times.Never());
        }
    }
}