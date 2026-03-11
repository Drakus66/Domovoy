namespace Domovoy.Common.Models.Devices;

/// <summary>
/// Configuration for sensor reporting intervals and change thresholds.
/// </summary>
public class SensorReportingConfig
{
    public TimeSpan ReportingInterval { get; set; } = TimeSpan.FromMinutes(5);
    public double? MinimumChange { get; set; }
    public bool ReportOnlyChanges { get; set; }
}
