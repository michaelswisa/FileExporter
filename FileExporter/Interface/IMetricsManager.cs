namespace FileExporter.Interface
{
    public interface IMetricsManager
    {
        void SetGaugeValue(string name, string description, string[] labelNames, string[] labelValues, double value);
        void RemoveGaugeSeries(string name, string[] labelValues);
    }
}
