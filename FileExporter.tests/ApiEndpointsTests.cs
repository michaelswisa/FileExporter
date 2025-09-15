using FileExporter.Models;
using FileExporter.Services;
using Moq;
using System.Net;

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
            // נוסיף איפוס של המוק לפני כל טסט כדי למנוע תלויות בין טסטים
            _scanManagerMock.Reset();
        }

        // שם הטסט עודכן לבהירות
        [Theory]
        [InlineData("all/my-dname")]
        [InlineData("failures/my-dname")]
        [InlineData("zombies/observed/my-dname")]
        [InlineData("zombies/non-observed/my-dname")]
        [InlineData("transcoded/my-dname")]
        public async Task Post_ScanEndpoints_WhenDirectoryFound_ShouldReturnAccepted(string endpoint)
        {
            // Arrange
            // שינוי: ה-Setup עודכן כך שיגדיר את התנהגות מתודות ה-Queue...Async החדשות.
            // הקונטרולר כבר לא קורא למתודות Scan...Async.
            _scanManagerMock.Setup(s => s.QueueFailureScanForDNameAsync(It.IsAny<string>())).ReturnsAsync(true);
            _scanManagerMock.Setup(s => s.QueueZombiesForDNameAsync(It.IsAny<string>(), It.IsAny<ZombieType>())).ReturnsAsync(true);
            _scanManagerMock.Setup(s => s.QueueTranscodedScanForDNameAsync(It.IsAny<string>())).ReturnsAsync(true);

            // ה-Setup עבור ScanAllTypesForDNameAsync נמחק כי הקונטרולר כבר לא קורא לו.
            // ה-endpoint של /all קורא לכל מתודת Queue בנפרד, והגדרות ה-Setup למעלה מכסות אותו.

            // Act
            var response = await _client.PostAsync($"/api/scan/{endpoint}", null);

            // Assert
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        // חדש: טסט שבודק את מקרה הקצה שבו תיקייה לא נמצאת וה-API מחזיר 404
        [Theory]
        [InlineData("failures/my-dname")]
        [InlineData("zombies/observed/my-dname")]
        public async Task Post_ScanEndpoints_WhenDirectoryNotFound_ShouldReturnNotFound(string endpoint)
        {
            // Arrange
            // שינוי: כאן אנו מגדירים שהמתודות יחזירו false, כאילו התיקייה לא נמצאה.
            _scanManagerMock.Setup(s => s.QueueFailureScanForDNameAsync(It.IsAny<string>())).ReturnsAsync(false);
            _scanManagerMock.Setup(s => s.QueueZombiesForDNameAsync(It.IsAny<string>(), It.IsAny<ZombieType>())).ReturnsAsync(false);
            _scanManagerMock.Setup(s => s.QueueTranscodedScanForDNameAsync(It.IsAny<string>())).ReturnsAsync(false);

            // Act
            var response = await _client.PostAsync($"/api/scan/{endpoint}", null);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // שם הטסט והמימוש עודכנו
        [Fact]
        public async Task Post_FailuresEndpoint_TriggersCorrectQueueMethod()
        {
            // Arrange
            var dName = "test-service";
            _scanManagerMock.Setup(s => s.QueueFailureScanForDNameAsync(dName)).ReturnsAsync(true);

            // Act
            await _client.PostAsync($"/api/scan/failures/{dName}", null);

            // Assert
            // שינוי: ה-Verify בודק שנקראה המתודה הנכונה - QueueFailureScanForDNameAsync
            _scanManagerMock.Verify(s => s.QueueFailureScanForDNameAsync(dName), Times.Once());

            // נוודא גם שלא נקראו בטעות מתודות אחרות
            _scanManagerMock.Verify(s => s.QueueZombiesForDNameAsync(It.IsAny<string>(), It.IsAny<ZombieType>()), Times.Never());
            _scanManagerMock.Verify(s => s.QueueTranscodedScanForDNameAsync(It.IsAny<string>()), Times.Never());
        }

        // חדש: טסט ייעודי שמוודא שנקודת הקצה /all מפעילה את כל הסריקות
        [Fact]
        public async Task Post_AllEndpoint_TriggersAllQueueMethods()
        {
            // Arrange
            var dName = "test-all";
            _scanManagerMock.Setup(s => s.QueueFailureScanForDNameAsync(dName)).ReturnsAsync(true);
            _scanManagerMock.Setup(s => s.QueueZombiesForDNameAsync(dName, ZombieType.Observed)).ReturnsAsync(true);
            _scanManagerMock.Setup(s => s.QueueZombiesForDNameAsync(dName, ZombieType.Non_Observed)).ReturnsAsync(true);
            _scanManagerMock.Setup(s => s.QueueTranscodedScanForDNameAsync(dName)).ReturnsAsync(true);

            // Act
            await _client.PostAsync($"/api/scan/all/{dName}", null);

            // Assert
            // נוודא שכל אחת מארבע מתודות ה-Queue נקראה בדיוק פעם אחת
            _scanManagerMock.Verify(s => s.QueueFailureScanForDNameAsync(dName), Times.Once());
            _scanManagerMock.Verify(s => s.QueueZombiesForDNameAsync(dName, ZombieType.Observed), Times.Once());
            _scanManagerMock.Verify(s => s.QueueZombiesForDNameAsync(dName, ZombieType.Non_Observed), Times.Once());
            _scanManagerMock.Verify(s => s.QueueTranscodedScanForDNameAsync(dName), Times.Once());
        }

        // טסט זה לא היה צריך להשתנות מכיוון שהוא לא קשור ללוגיקת הסריקה
        [Fact]
        public async Task Get_MetricsEndpoint_ShouldReturnOkAndPrometheusContent()
        {
            // Act
            var response = await _client.GetAsync("/metrics");
            var content = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.StartsWith("# HELP", content);
            Assert.Contains("text/plain", response.Content.Headers.ContentType?.ToString() ?? string.Empty);
        }
    }
}