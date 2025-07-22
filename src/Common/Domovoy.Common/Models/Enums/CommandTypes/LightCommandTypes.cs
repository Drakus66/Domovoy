namespace Domovoy.Common.Models.Enums;

/// <summary>
/// Defines the types of commands that can be sent to light devices.
/// </summary>
public enum LightCommandTypes
{
    /// <summary>
    /// Command to turn on the light.
    /// </summary>
    TurnOn,

    /// <summary>
    /// Command to turn off the light.
    /// </summary>
    TurnOff,

    /// <summary>
    /// Command to set the brightness level of the light.
    /// </summary>
    SetBrightness,

    /// <summary>
    /// Command to set the color of the light.
    /// </summary>
    SetColor,

    /// <summary>
    /// Command to set the color temperature of the light.
    /// </summary>
    SetColorTemperature
}