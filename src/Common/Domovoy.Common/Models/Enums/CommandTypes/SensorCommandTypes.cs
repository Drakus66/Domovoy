namespace Domovoy.Common.Models.Enums;

/// <summary>
/// Defines the types of commands that can be sent to sensor devices.
/// </summary>
public enum SensorCommandTypes
{
    /// <summary>
    /// Command to request an immediate sensor reading update.
    /// </summary>
    RequestUpdate,

    /// <summary>
    /// Command to configure the sensor's reporting settings.
    /// </summary>
    ConfigureReporting,

    /// <summary>
    /// Command to calibrate the sensor.
    /// </summary>
    Calibrate,

    /// <summary>
    /// Command to clear the sensor's reading history.
    /// </summary>
    ClearHistory
}