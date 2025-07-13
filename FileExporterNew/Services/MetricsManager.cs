using System.Collections.Concurrent;
using Prometheus;

namespace FileExporterNew.Services
{
    public class MetricsManager
    {
        private readonly ILogger<MetricsManager> _logger;
        private static readonly ConcurrentDictionary<string, Gauge> _gauges = new();

        public MetricsManager(ILogger<MetricsManager> logger)
        {
            _logger = logger;
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
                        LabelNames = labelName
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
    }
}
