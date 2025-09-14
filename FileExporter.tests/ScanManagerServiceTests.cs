using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FileExporter.Models;
using FileExporter.Services;
using FileExporter.Interface;

namespace FileExporter.tests
{
    public class ScanManagerServiceTests
    {
        private readonly Mock<ILogger<ScanManagerService>> _loggerMock;
        private readonly Mock<IOptions<Settings>> _settingsMock;
        private readonly Mock<IFailureSearchService> _failureSearcherMock;
        private readonly Mock<IZombieSearchService> _zombieSearcherMock;
        private readonly Mock<ITranscodedSearchService> _transcodedSearcherMock;
        private readonly Mock<IFileHelper> _fileHelperMock;
        private readonly ScanManagerService _scanManager;

        public ScanManagerServiceTests()
        {
            _loggerMock = new Mock<ILogger<ScanManagerService>>();

            var settings = new Settings { Env = "prod" };
            _settingsMock = new Mock<IOptions<Settings>>();
            _settingsMock.Setup(s => s.Value).Returns(settings);

            _failureSearcherMock = new Mock<IFailureSearchService>();
            _zombieSearcherMock = new Mock<IZombieSearchService>();
            _transcodedSearcherMock = new Mock<ITranscodedSearchService>();
            _fileHelperMock = new Mock<IFileHelper>();

            _scanManager = new ScanManagerService(
                _loggerMock.Object,
                _settingsMock.Object,
                _failureSearcherMock.Object,
                _zombieSearcherMock.Object,
                _transcodedSearcherMock.Object,
                _fileHelperMock.Object
            );
        }

        [Theory]
        [InlineData("my-service-landing-dir-prod", "my-service", "prod")]
        [InlineData("another-complex-service-name-landing-dir-int", "another-complex-service-name", "int")]
        [InlineData("short-landing-dir-dev", "short", "dev")]
        [InlineData("UPPERCASE-SERVICE-landing-dir-prod", "UPPERCASE-SERVICE", "prod")]
        public void ParseAndValidateDirectoryName_WithValidDirectoryNames_ShouldReturnCorrectTuple(string dirName, string expectedDName, string expectedEnv)
        {
            // Act
            var result = _scanManager.ParseAndValidateDirectoryName(dirName);

            // Assert
            Assert.NotNull(result);
            var (actualDName, actualEnv) = result.Value; // This should solve the warning

            Assert.Equal(expectedDName, actualDName);
            Assert.Equal(expectedEnv, actualEnv, ignoreCase: true);
        }

        [Theory]
        [InlineData("my-service-prod")]
        [InlineData("my-service-landing-dir-uat")]
        [InlineData("landing-dir-prod")]
        [InlineData("my-service-landing-dir-")]
        [InlineData("some-random-string")]
        [InlineData("")]
        [InlineData(null)]
        public void ParseAndValidateDirectoryName_WithInvalidDirectoryNames_ShouldReturnNull(string? dirName) // Fix warning by making type nullable
        {
            // Act
            var result = _scanManager.ParseAndValidateDirectoryName(dirName);

            // Assert
            Assert.Null(result);
        }
    }
}