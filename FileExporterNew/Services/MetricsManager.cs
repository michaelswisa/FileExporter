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

        public void SetGaugeValue(string name, string description, string[] labelNames, string[] labelValues, double value)
        {
            _logger.LogInformation($"Setting gauge value for metric: {name}, value: {value}, labels: {string.Join(", ", labelValues)}");
            try
            {
                var gauge = _gauges.GetOrAdd(name, _ =>
                    Metrics.CreateGauge(name, description, new GaugeConfiguration
                    {
                        LabelNames = labelNames,
                        SuppressInitialValue = true
                    }));

                gauge.WithLabels(labelValues).Set(value);
                _logger.LogInformation($"Successfully set gauge value for metric: {name}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating metric: {name}. Error: {ex.Message}");
                throw;
            }
        }

        public void RemoveGaugeSeries(string name, string[] labelValues)
        {
            _logger.LogInformation($"Attempting to remove gauge series for metric: {name}, labels: {string.Join(", ", labelValues)}");
            if (_gauges.TryGetValue(name, out var gauge))
            {
                gauge.RemoveLabelled(labelValues);
                _logger.LogInformation($"Successfully removed gauge series for metric: {name}");
            }
            else
            {
                _logger.LogWarning($"Gauge metric '{name}' not found, cannot remove series.");
            }
        }
    }
}
