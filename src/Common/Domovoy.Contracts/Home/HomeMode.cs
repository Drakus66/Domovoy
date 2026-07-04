namespace Domovoy.Contracts.Home;

/// <summary>
/// Home mode / presence context (roadmap Epic 1G). The mode is an explicit, low-cardinality context
/// (Home / Away / Night / Vacation) that triggers, conditions and climate read, and that the P0-5
/// event-log stamps onto every record as an ML feature. Modelled as an <b>open</b> set of strings (like
/// capabilities and trigger sources) rather than a closed enum, so deployments can add their own modes
/// without a contract change. Matched case-insensitively across the system.
/// </summary>
public static class WellKnownModes
{
    /// <summary>Someone is home — normal interactive behaviour.</summary>
    public const string Home = "Home";

    /// <summary>Nobody home — energy-saving setbacks, security-leaning automations.</summary>
    public const string Away = "Away";

    /// <summary>Night — dimmed/quiet behaviour while occupants sleep.</summary>
    public const string Night = "Night";

    /// <summary>Extended absence — deeper setbacks than Away; not auto-switched by presence.</summary>
    public const string Vacation = "Vacation";

    /// <summary>The default mode when no state has ever been set.</summary>
    public const string Default = Home;

    /// <summary>Canonical ordering for UI; deployments may surface additional custom modes too.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Home, Away, Night, Vacation };

    /// <summary>
    /// Modes that presence may auto-switch between. Night/Vacation are deliberately manual — presence
    /// must never override an occupant's explicit choice (see PresenceMonitor in the AutomationService).
    /// </summary>
    public static bool IsPresenceManaged(string? mode) =>
        string.Equals(mode, Home, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mode, Away, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Well-known sources that changed the home mode (open set, like trigger sources in P0-5).</summary>
public static class ModeChangeSources
{
    public const string User = "user";
    public const string Presence = "presence";
    public const string Rule = "rule";
    public const string Ml = "ml";
}
