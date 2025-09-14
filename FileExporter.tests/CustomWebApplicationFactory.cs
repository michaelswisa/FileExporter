using FileExporter.Interface;
using FileExporter.Models;
using FileExporter.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions; // Add this for TryRemove
using Microsoft.Extensions.Logging; // Add this for ILogger
using Microsoft.Extensions.Options; // Add this for IOptions
using Moq;

namespace FileExporter.tests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        public Mock<ScanManagerService> ScanManagerMock { get; }

        public CustomWebApplicationFactory()
        {
            // This mock is for the main service we want to control and verify.
            ScanManagerMock = new Mock<ScanManagerService>(
                Mock.Of<ILogger<ScanManagerService>>(),
                Mock.Of<IOptions<Settings>>(),
                Mock.Of<IFailureSearchService>(),
                Mock.Of<IZombieSearchService>(),
                Mock.Of<ITranscodedSearchService>(),
                Mock.Of<IFileHelper>()
            );
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // 1. Remove the original registrations to avoid conflicts.
                // Using TryRemove is safer than Remove.
                services.RemoveAll<ScanManagerService>();
                services.RemoveAll<IFailureSearchService>();
                services.RemoveAll<IZombieSearchService>();
                services.RemoveAll<ITranscodedSearchService>();
                services.RemoveAll<IFileHelper>();
                services.RemoveAll<IMetricsManager>();

                // 2. Add our mocks instead.

                // The main service we are testing against.
                services.AddSingleton(ScanManagerMock.Object);

                // Add mocks for all other dependencies that the DI container needs to build the host.
                // Even if we don't use them directly in the test, the DI container needs to know how to build them.
                services.AddSingleton(Mock.Of<IFailureSearchService>());
                services.AddSingleton(Mock.Of<IZombieSearchService>());
                services.AddSingleton(Mock.Of<ITranscodedSearchService>());
                services.AddSingleton(Mock.Of<IFileHelper>());
                services.AddSingleton(Mock.Of<IMetricsManager>());
            });
        }
    }
}