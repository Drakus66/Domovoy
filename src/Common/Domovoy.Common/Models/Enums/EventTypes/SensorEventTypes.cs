namespace Domovoy.Common.Models.Enums;

/// <summary>
/// Defines the types of events that can be emitted by sensor devices.
/// </summary>
public enum SensorEventTypes
{
    /// <summary>
    /// Event indicating that new sensor data has been received.
    /// </summary>
    DataReceived,

    /// <summary>
    /// Event indicating that the sensor's state has changed.
    /// </summary>
    StateChanged,

    /// <summary>
    /// Event indicating that a sensor threshold has been exceeded.
    /// </summary>
    ThresholdExceeded,

    /// <summary>
    /// Event indicating an error condition.
    /// </summary>
    Error,

    /// <summary>
    /// Event indicating a response to a command.
    /// </summary>
    CommandResponse
}