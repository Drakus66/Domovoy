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

    /// <summary>
    /// Minimum seconds between executions of a BoundedActive rule (roadmap Epic 1F staged rollout). A
    /// promoted-but-unproven rule runs, but no more often than this — bounding actuation rate while trust builds.
    /// </summary>
    public int BoundedActiveCooldownSeconds { get; set; } = 300;

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

    // --- Per-zone model scoping (roadmap Epic 2I) ---

    /// <summary>
    /// When true, training also fits shared per-zone-kind models and, where they earn it, per-zone models —
    /// so a thermostat resolves its model along the zone → zone_kind → global chain. The global model is
    /// always trained; this only adds the more specific scopes.
    /// </summary>
    public bool TrainZoneModels { get; set; } = true;

    /// <summary>
    /// How much a per-zone candidate must beat its fallback (zone_kind/global) on holdout to be registered
    /// (auto-promotion gate, in the template's metric units). Keeps a zone on the shared model until its own
    /// behaviour genuinely diverges, instead of splintering on noise.
    /// </summary>
    public double ZonePromotionMargin { get; set; } = 0.25;

    // --- Heuristic rule proposer (roadmap Epic 2C; explicit stub-precursor to 2F) ---

    /// <summary>How often the heuristic proposer scans the event-log for candidate rules (0 disables the periodic scan).</summary>
    public int ProposalScanHours { get; set; } = 6;

    /// <summary>History window (days) the proposer mines for trigger→action co-occurrences.</summary>
    public int ProposalWindowDays { get; set; } = 14;

    /// <summary>Max seconds after a sensor trigger within which a human action counts as "following" it.</summary>
    public int ProposalCoWindowSeconds { get; set; } = 120;

    /// <summary>Minimum times a pattern must recur (support) before it is proposed.</summary>
    public int ProposalMinSupport { get; set; } = 3;

    /// <summary>Minimum P(action | trigger) (confidence) before a pattern is proposed.</summary>
    public double ProposalMinConfidence { get; set; } = 0.6;

    /// <summary>Sensor capabilities whose "became active" transition is treated as a candidate trigger.</summary>
    public string[] ProposalTriggerCapabilities { get; set; } = { "presence", "occupancy", "motion" };

    // --- Pattern-discovery engine (roadmap Epic 2F; the full MI/FDR funnel over the 2C heuristic) ---

    /// <summary>How often the discovery engine scans history for patterns (0 disables the periodic scan; manual endpoint still works).</summary>
    public int DiscoveryScanHours { get; set; } = 12;

    /// <summary>History window (days) the discovery engine mines.</summary>
    public int DiscoveryWindowDays { get; set; } = 21;

    /// <summary>Slot size (seconds) history is bucketed into for transition-aligned MI screening.</summary>
    public int DiscoverySlotSeconds { get; set; } = 300;

    /// <summary>Sensor capabilities considered as candidate drivers — booleans and numerics (illuminance for "dark→light", co2, temperature…).</summary>
    public string[] DiscoverySensorCapabilities { get; set; } =
        { "presence", "occupancy", "motion", "contact", "illuminance", "temperature", "co2", "humidity" };

    /// <summary>Benjamini-Hochberg target false-discovery rate for the screening stage.</summary>
    public double DiscoveryFdrQ { get; set; } = 0.05;

    /// <summary>Minimum times a mined condition must co-occur with the action (support).</summary>
    public int DiscoveryMinSupport { get; set; } = 4;

    /// <summary>Minimum P(action | condition) (confidence) before a pattern is proposed.</summary>
    public double DiscoveryMinConfidence { get; set; } = 0.6;

    /// <summary>Minimum lift (confidence ÷ base rate) — how much the condition beats the action's overall frequency.</summary>
    public double DiscoveryMinLift { get; set; } = 1.5;

    /// <summary>Max candidates queued per scan (a noisy window can't flood the approval queue).</summary>
    public int DiscoveryMaxProposals { get; set; } = 10;

    /// <summary>
    /// Granger-causality gate (roadmap Epic 2F): a screened pair also has to show the sensor's <i>previous</i>
    /// slot predicts the action <i>now</i> at this significance (p ≤ alpha). Kills co-variation without temporal
    /// precedence. <b>Opt-in (0 disables)</b> — it needs denser consecutive-slot history than the as-of MI screen,
    /// so it can over-prune sparse homelab data; enable it once enough history has accrued.
    /// </summary>
    public double DiscoveryGrangerAlpha { get; set; } = 0.0;

    // --- Setpoint-preference mining (roadmap Epic 2F, type B — learned numeric setpoints) ---

    /// <summary>Numeric writable capabilities whose repeated user settings are mined into a scheduled preference.</summary>
    public string[] SetpointPreferenceCapabilities { get; set; } = { "temperature_setpoint" };

    /// <summary>Minimum times a user set a setpoint in a time-of-day bucket before a preference is proposed.</summary>
    public int SetpointMinSupport { get; set; } = 4;

    /// <summary>Max standard deviation (in the setpoint's unit) for a bucket's settings to count as a stable preference.</summary>
    public double SetpointMaxStdDev { get; set; } = 1.0;

    // --- Composite control blocks (roadmap Epic 1H E2) ---

    /// <summary>
    /// Declarative composite block types (roadmap Epic 1H E2), authored via the pipe DSL — a new composite is a
    /// config entry, not code. Parsed against the built-in primitives and added to the catalog at startup.
    /// </summary>
    public CompositeSpec[]? Composites { get; set; }
}

/// <summary>A config-authored composite block: a stable type id + the pipe DSL that defines its subgraph.</summary>
public sealed class CompositeSpec
{
    public string TypeId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }

    /// <summary>Pipe DSL, e.g. <c>input(temperature) |&gt; ewma_filter(tau=300) |&gt; thermostat(setpoint=21)</c>.</summary>
    public string? Dsl { get; set; }
}
