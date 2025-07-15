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
            try
            {
                var gauge = _gauges.GetOrAdd(name, _ => 
                    Metrics.CreateGauge(name, description, new GaugeConfiguration
                    {
                        LabelNames = labelNames,
                        SuppressInitialValue = true
                    }));

                gauge.WithLabels(labelValues).Set(value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating metric: {Name}", name);
                throw;
            }
        }

        public void RemoveGaugeSeries(string name, string[] labelValues)
        {
            if (_gauges.TryGetValue(name, out var gauge))
            {
                gauge.RemoveLabelled(labelValues);
            }
        }
    }
}