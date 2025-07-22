namespace Domovoy.Common.Models.Events;

/// <summary>
/// Base class for all events in the Domovoy system.
/// Provides common properties and functionality for event messages.
/// </summary>
public abstract class BaseEvent
{
    /// <summary>
    /// Gets or sets the correlation identifier for this event.
    /// This property identifies the operation or command that triggered this event.
    /// </summary>
    public Guid CorrelationId { get; set; }

    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets a value indicating whether the operation that generated this event was successful.
    /// </summary>
    public bool Success { get; set; }

    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the error message if the operation failed.
    /// </summary>
    public string? Error { get; set; }
}
