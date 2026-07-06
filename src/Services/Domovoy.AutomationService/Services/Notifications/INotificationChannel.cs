namespace Domovoy.AutomationService.Services.Notifications;

/// <summary>A notification to deliver. Severity is a free string (info/warning/critical) for channel formatting.</summary>
public sealed record NotificationMessage(string Title, string Body, string Severity = "info");

/// <summary>
/// A delivery channel for notifications (roadmap Epic 2G). Provider-agnostic: Telegram, generic webhook/push,
/// … each channel is independently enabled by config. The dispatcher fans a message out to every enabled
/// channel; a channel that is disabled or fails does not affect the others or rule execution.
/// </summary>
public interface INotificationChannel
{
    /// <summary>Short channel name (e.g. "telegram", "webhook") — surfaced in status and logs.</summary>
    string Name { get; }

    /// <summary>Whether the channel is configured and turned on.</summary>
    bool Enabled { get; }

    /// <summary>Deliver the message. Returns true on success; must not throw for a delivery failure.</summary>
    Task<bool> SendAsync(NotificationMessage message, CancellationToken ct);
}
