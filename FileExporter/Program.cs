using FileExporter;
using FileExporter.Interface;
using FileExporter.Models;
using FileExporter.Services;
using Prometheus;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<Settings>(builder.Configuration.GetSection("Settings"));

        builder.Services.AddSingleton<IFileHelper, FileHelper>();
        builder.Services.AddSingleton<IMetricsManager, MetricsManager>();
        builder.Services.AddSingleton<IFailureSearchService, FailureSearchService>();
        builder.Services.AddSingleton<IZombieSearchService, ZombieSearchService>();
        builder.Services.AddSingleton<ITranscodedSearchService, TranscodedSearchService>();
        builder.Services.AddSingleton<ITraversalService, TraversalService>();
        builder.Services.AddScoped<ScanManagerService>();

        builder.Services.AddHostedService<FileScanningWorker>();

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseHttpsRedirection();
        app.UseRouting();
        app.MapMetrics();
        app.UseAuthorization();
        app.MapControllers();

        app.Run();
    }
}