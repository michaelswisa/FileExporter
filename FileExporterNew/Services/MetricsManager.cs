using System.Collections.Concurrent;
using Prometheus;

namespace FileExporterNew.Services
{
    public class MetricsManager : IDisposable
    {
        private readonly ILogger<MetricsManager> _logger;
        private static readonly ConcurrentDictionary<string, Gauge> _gauges = new();
        private readonly Timer _cleanupTimer;

        public MetricsManager(ILogger<MetricsManager> logger)
        {
            _logger = logger;
            // Cleanup old metrics every 30 minutes
            _cleanupTimer = new Timer(CleanupOldMetrics, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
        }

        public void SetGaugeValue(string name, string description, string[] labelName, string[] labelValue, double value)
        {
            try
            {
                var gauge = _gauges.GetOrAdd(name, _ =>
                {
                    _logger.LogInformation($"Creating new metric: {name}");
                    return Metrics.CreateGauge(name, description, new GaugeConfiguration
                    {
                        LabelNames = labelName,
                        SuppressInitialValue = true // ✅ שיפור: לא מייצר child ריק
                    });
                });

                gauge.WithLabels(labelValue).Set(value);
                _logger.LogInformation($"Updated metric: {name}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating metric: {name}, Error: {ex}");
                throw;
            }
        }

        // ✅ פונקציה חדשה: מחיקת סדרה בודדת לפי ערכי label
        public void RemoveGaugeSeries(string name, string[] labelValues)
        {
            if (_gauges.TryGetValue(name, out var gauge))
            {
                gauge.RemoveLabelled(labelValues);
                _logger.LogInformation($"Removed series: {name} [{string.Join(',', labelValues)}]");
            }
        }

        private void CleanupOldMetrics(object? state)
        {
            try
            {
                _logger.LogDebug("Running metrics cleanup");
                // This is a placeholder for future cleanup logic if needed
                // Currently, Prometheus handles most cleanup automatically
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during metrics cleanup");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }
}
