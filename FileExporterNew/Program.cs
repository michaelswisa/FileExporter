using FileExporterNew.Models;
using FileExporterNew.Services;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<Settings>(builder.Configuration.GetSection("Settings"));
builder.Services.AddSingleton<MetricsManager>();
builder.Services.AddScoped<FailureSearchService>();
builder.Services.AddScoped<ZombieSearchService>();
builder.Services.AddScoped<FileHelper>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.UseMetricServer();

app.MapControllers();

app.Run();
