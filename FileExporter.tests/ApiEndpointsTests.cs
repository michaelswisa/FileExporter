using System.Net;
using FileExporter.Services;
using Moq;

namespace FileExporter.tests
{
    public class ApiEndpointsTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly HttpClient _client;
        private readonly Mock<ScanManagerService> _scanManagerMock;

        public ApiEndpointsTests(CustomWebApplicationFactory factory)
        {
            _client = factory.CreateClient();
            _scanManagerMock = factory.ScanManagerMock;
        }

        [Theory]
        [InlineData("all/my-dname")]
        [InlineData("failures/my-dname")]
        [InlineData("zombies/observed/my-dname")]
        [InlineData("zombies/non-observed/my-dname")]
        [InlineData("transcoded/my-dname")]
        public async Task Post_ScanEndpoints_ShouldReturnAccepted(string endpoint)
        {
            // Arrange
            _scanManagerMock.Setup(s => s.ScanAllTypesForDNameAsync(It.IsAny<string>())).ReturnsAsync(true);
            _scanManagerMock.Setup(s => s.ScanFailuresForDNameAsync(It.IsAny<string>())).ReturnsAsync(true);
            _scanManagerMock.Setup(s => s.ScanZombiesForDNameAsync(It.IsAny<string>(), It.IsAny<ZombieType>())).ReturnsAsync(true);
            _scanManagerMock.Setup(s => s.ScanTranscodedForDNameAsync(It.IsAny<string>())).ReturnsAsync(true);

            // Act
            var response = await _client.PostAsync($"/api/scan/{endpoint}", null);

            // Assert
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        [Fact]
        public async Task Post_TriggersCorrectScanManagerMethod()
        {
            // Arrange
            var dName = "test-service";
            _scanManagerMock.Reset();

            // Act
            await _client.PostAsync($"/api/scan/failures/{dName}", null);

            // Assert
            _scanManagerMock.Verify(s => s.ScanFailuresForDNameAsync(dName), Times.Once());
            _scanManagerMock.Verify(s => s.ScanAllTypesForDNameAsync(It.IsAny<string>()), Times.Never());
        }

        [Fact]
        public async Task Get_MetricsEndpoint_ShouldReturnOkAndPrometheusContent()
        {
            // Act
            var response = await _client.GetAsync("/metrics");
            var content = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.StartsWith("# HELP", content);
            // Fix for warning CS8602: Dereference of a possibly null reference.
            // Content.Headers.ContentType can be null.
            Assert.Contains("text/plain", response.Content.Headers.ContentType?.ToString() ?? string.Empty);
        }
    }
}