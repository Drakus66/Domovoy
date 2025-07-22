namespace Domovoy.Common.Models.Enums;

/// <summary>
/// Defines the types of events that can be emitted by light devices.
/// </summary>
public enum LightEventTypes
{
    /// <summary>
    /// Event indicating that the light's state has changed.
    /// </summary>
    StateChanged,

    /// <summary>
    /// Event indicating a response to a command.
    /// </summary>
    CommandResponse,

    /// <summary>
    /// Event indicating an error condition.
    /// </summary>
    Error
}