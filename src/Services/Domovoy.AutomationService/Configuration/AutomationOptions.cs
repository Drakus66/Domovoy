namespace Domovoy.AutomationService.Configuration;

/// <summary>
/// AutomationService configuration (roadmap Epic 1A). Bound from the <c>Automation</c> config section.
/// </summary>
public class AutomationOptions
{
    public const string SectionName = "Automation";

    /// <summary>DbGateway base URL — used to load user rules and the device read-model (zone/state).</summary>
    public string DbGatewayBaseUrl { get; set; } = "http://db-gateway:8080";

    /// <summary>Site latitude for sunrise/sunset triggers (outdoor lighting).</summary>
    public double Latitude { get; set; } = 55.7558;

    /// <summary>Site longitude for sunrise/sunset triggers.</summary>
    public double Longitude { get; set; } = 37.6173;

    /// <summary>How often to reload rules + device read-model from the DbGateway.</summary>
    public int RefreshSeconds { get; set; } = 30;

    /// <summary>Path to the local safety-floor rules file (protected, non-disableable from UI).</summary>
    public string SafetyRulesPath { get; set; } = "safety-rules.json";

    // --- Presence-driven home mode (roadmap Epic 1G) ---

    /// <summary>
    /// When true, presence sensors auto-switch the home mode between Home and Away (never Night/Vacation,
    /// which are manual). Disable to keep the mode purely manual.
    /// </summary>
    public bool PresenceAutoMode { get; set; } = true;

    /// <summary>Capability ids treated as presence/occupancy signals.</summary>
    public string[] PresenceCapabilities { get; set; } = { "presence", "occupancy" };

    /// <summary>How long all presence signals must stay clear before switching Home → Away.</summary>
    public int AwayDelaySeconds { get; set; } = 600;

    // --- ML substrate (roadmap Epic 2A) ---

    /// <summary>Capability the schedule model is trained to predict (and the ML-setpoint block emits for).</summary>
    public string TrainCapability { get; set; } = "temperature";

    /// <summary>History window (days) pulled from telemetry for training.</summary>
    public int TrainWindowDays { get; set; } = 30;

    /// <summary>Minimum samples required to train (cold-start guard).</summary>
    public int MinSamples { get; set; } = 20;

    /// <summary>How often to retrain automatically.</summary>
    public int TrainIntervalHours { get; set; } = 24;

    /// <summary>How often to check the registry for a newer model to load for inference.</summary>
    public int ModelRefreshMinutes { get; set; } = 10;

    /// <summary>Safety clamp on the ML-predicted setpoint (°C).</summary>
    public double SetpointMin { get; set; } = 16;
    public double SetpointMax { get; set; } = 26;
}
