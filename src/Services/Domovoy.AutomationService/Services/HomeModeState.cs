using Domovoy.Contracts.Home;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Thread-safe holder of the current home mode (roadmap Epic 1G). Seeded from the DbGateway at startup
/// (<see cref="RefreshLoop"/>) and kept live by <see cref="HomeModeMonitor"/> from
/// <see cref="Domovoy.Contracts.Messaging.HomeModeChangedV1"/>. The <see cref="RuleRunner"/> reads it so
/// <c>Mode</c> conditions evaluate against reality; the <see cref="PresenceMonitor"/> reads it to decide
/// whether presence may auto-switch (it never overrides a manual Night/Vacation).
/// </summary>
public sealed class HomeModeState
{
    private volatile string _current = WellKnownModes.Default;

    public string Current => _current;

    /// <summary>Replace the current mode (no-op for null/blank). Returns the effective mode.</summary>
    public string Set(string? mode)
    {
        if (!string.IsNullOrWhiteSpace(mode))
            _current = mode.Trim();
        return _current;
    }
}
