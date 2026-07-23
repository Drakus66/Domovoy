// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Home;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

using LoadSheddingProfileSnapshot = Domovoy.AutomationService.Services.DbGatewayClient.LoadSheddingProfileSnapshot;

namespace Domovoy.AutomationService.Services;

/// <summary>How far a managed load has been pulled back from normal operation.</summary>
public enum ShedLevel { Normal, Curtailed, Off }

/// <summary>
/// A managed load eligible for shedding: its read-model name, its shedding profile and the watts the two
/// operating points cost. The power figures come from the device's <b>energy profile</b> (Epic 3C-D) — the
/// single place watts are configured — resolved once in <see cref="LoadManager.Sync"/>.
/// </summary>
public sealed record LoadCandidate(
    Guid DeviceId, string Name, LoadSheddingProfileSnapshot Profile, double NominalPowerW, double? CurtailedPowerW,
    string? CircuitId = null, string? Phase = null);

/// <summary>One entry on the shed stack — a level transition LoadManager applied, restored in reverse.</summary>
public sealed record ShedStackEntry(Guid DeviceId, ShedLevel From, ShedLevel To, DateTime AtUtc);

/// <summary>A single capability command to publish (shed or restore step).</summary>
public sealed record LoadCommand(Guid DeviceId, string Name, string CapabilityId, object? Value, bool IsRestore);

public sealed record ShedPlanResult(
    IReadOnlyList<LoadCommand> Commands, IReadOnlyList<ShedStackEntry> Pushed, double EstimatedWatts, bool StillOver);

/// <summary>Restores to apply, the stack entries they unwind, and the watts they collectively give back.</summary>
public sealed record RestorePlanResult(
    IReadOnlyList<LoadCommand> Commands, IReadOnlyList<ShedStackEntry> Popped, double RestoredWatts);

/// <summary>
/// Pure shed/restore decision logic for <see cref="LoadManager"/> (roadmap Epic 3C-LM) — no bus/DB, so it
/// is directly unit-testable. <see cref="LoadManager"/> wraps this with device I/O (publishing
/// <see cref="DeviceCommandV1"/>, notifications) and the mutable runtime state (current levels + stack).
/// </summary>
public static class LoadShedPlanner
{
    public const string TierCritical = "critical";
    public const string TierSheddable = "sheddable";
    public const string TierUnmanaged = "unmanaged";

    /// <summary>Power (W) a level transition frees (shed) or gives back (restore) — same magnitude either way.</summary>
    public static double EstimateFreedWatts(LoadCandidate load, ShedLevel from, ShedLevel to)
    {
        var nominal = load.NominalPowerW;
        var curtailed = load.CurtailedPowerW ?? 0;
        return (from, to) switch
        {
            (ShedLevel.Normal, ShedLevel.Curtailed) => Math.Max(0, nominal - curtailed),
            (ShedLevel.Normal, ShedLevel.Off) => nominal,
            (ShedLevel.Curtailed, ShedLevel.Off) => curtailed,
            _ => 0,
        };
    }

    public static string Tier(LoadSheddingProfileSnapshot profile, string mode) =>
        profile.ModeTier.TryGetValue(mode, out var t) ? t : TierUnmanaged;

    public static int Priority(LoadSheddingProfileSnapshot profile, string mode) =>
        profile.ModePriority.TryGetValue(mode, out var p) ? p : int.MaxValue;

    /// <summary>Enabled, non-protected loads whose current-mode tier is "sheddable", cheapest-priority first
    /// (ties broken by the biggest nominal draw, then device id for determinism).</summary>
    public static IReadOnlyList<LoadCandidate> SheddableCandidates(IEnumerable<LoadCandidate> loads, string mode) =>
        loads
            .Where(l => l.Profile.Enabled && !l.Profile.Protected && Tier(l.Profile, mode) == TierSheddable)
            .OrderBy(l => Priority(l.Profile, mode))
            .ThenByDescending(l => l.NominalPowerW)
            .ThenBy(l => l.DeviceId)
            .ToList();

    /// <summary>
    /// Plan shed actions to bring <paramref name="measuredWatts"/> at/under <paramref name="limitWatts"/>:
    /// curtail-first (only loads with a known <see cref="LoadSheddingProfileSnapshot.CurtailedPowerW"/>
    /// estimate), then turn fully off, both in priority order. Never revisits an already-Off load.
    /// </summary>
    public static ShedPlanResult PlanShed(
        IReadOnlyList<LoadCandidate> candidates, IReadOnlyDictionary<Guid, ShedLevel> levels,
        double measuredWatts, double limitWatts, DateTime nowUtc)
    {
        var commands = new List<LoadCommand>();
        var pushed = new List<ShedStackEntry>();
        var estimated = measuredWatts;
        var working = new Dictionary<Guid, ShedLevel>(levels);

        // Phase 1 — curtail candidates with a known curtailed-power estimate.
        foreach (var c in candidates)
        {
            if (estimated <= limitWatts) break;
            if (working.GetValueOrDefault(c.DeviceId, ShedLevel.Normal) != ShedLevel.Normal) continue;
            if (!c.Profile.Curtailable || !c.CurtailedPowerW.HasValue) continue;

            var freed = EstimateFreedWatts(c, ShedLevel.Normal, ShedLevel.Curtailed);
            commands.Add(new LoadCommand(c.DeviceId, c.Name, c.Profile.ControlCapabilityId, c.Profile.CurtailedValue, IsRestore: false));
            pushed.Add(new ShedStackEntry(c.DeviceId, ShedLevel.Normal, ShedLevel.Curtailed, nowUtc));
            working[c.DeviceId] = ShedLevel.Curtailed;
            estimated -= freed;
        }

        // Phase 2 — turn fully off (curtailed-but-still-over loads included).
        foreach (var c in candidates)
        {
            if (estimated <= limitWatts) break;
            var level = working.GetValueOrDefault(c.DeviceId, ShedLevel.Normal);
            if (level == ShedLevel.Off) continue;

            var freed = EstimateFreedWatts(c, level, ShedLevel.Off);
            commands.Add(new LoadCommand(c.DeviceId, c.Name, CapabilityIds.OnOff, false, IsRestore: false));
            pushed.Add(new ShedStackEntry(c.DeviceId, level, ShedLevel.Off, nowUtc));
            working[c.DeviceId] = ShedLevel.Off;
            estimated -= freed;
        }

        return new ShedPlanResult(commands, pushed, estimated, estimated > limitWatts);
    }

    /// <summary>
    /// Plan restores against a single household budget — the common case, kept as a thin wrapper over the
    /// scope-aware overload below.
    /// </summary>
    public static RestorePlanResult PlanRestore(
        IReadOnlyList<ShedStackEntry> stack, IReadOnlyDictionary<Guid, LoadCandidate> loadsById,
        double measuredWatts, double limitWatts, double marginWatts, int minDwellSeconds, DateTime nowUtc) =>
        PlanRestore(stack, loadsById, _ => (measuredWatts, limitWatts), marginWatts, minDwellSeconds, nowUtc);

    /// <summary>
    /// Plan restores by popping the shed stack strictly in reverse (LIFO) order: stops at the first entry
    /// that either hasn't dwelled <paramref name="minDwellSeconds"/> yet, or whose restore would eat the
    /// required <paramref name="marginWatts"/> headroom — it never skips ahead to an older entry.
    /// <para><paramref name="headroomOf"/> answers "what is measured and what is allowed <b>for this load</b>":
    /// with an electrical topology (Epic 3C-D) that is the tightest of the household budget, its phase and its
    /// circuit, so a load never comes back onto a line that is still over its breaker.</para>
    /// </summary>
    public static RestorePlanResult PlanRestore(
        IReadOnlyList<ShedStackEntry> stack, IReadOnlyDictionary<Guid, LoadCandidate> loadsById,
        Func<LoadCandidate, (double Measured, double Limit)> headroomOf,
        double marginWatts, int minDwellSeconds, DateTime nowUtc)
    {
        var commands = new List<LoadCommand>();
        var popped = new List<ShedStackEntry>();
        var estimated = 0d;
        var remaining = new List<ShedStackEntry>(stack);

        while (remaining.Count > 0)
        {
            var top = remaining[^1];

            // A load dropped from the config since it was shed — drop the stale entry and keep unwinding.
            if (!loadsById.TryGetValue(top.DeviceId, out var load))
            {
                remaining.RemoveAt(remaining.Count - 1);
                popped.Add(top);
                continue;
            }

            if ((nowUtc - top.AtUtc).TotalSeconds < minDwellSeconds) break;

            var (measuredWatts, limitWatts) = headroomOf(load);
            var freed = EstimateFreedWatts(load, top.From, top.To);
            // `estimated` accumulates what earlier restores in this pass already gave back, so a batch of
            // restores can't collectively overshoot the limit.
            if (measuredWatts + estimated + freed + marginWatts > limitWatts) break;

            if (top.To == ShedLevel.Off)
            {
                commands.Add(new LoadCommand(top.DeviceId, load.Name, CapabilityIds.OnOff, true, IsRestore: true));
                if (top.From == ShedLevel.Curtailed)
                    commands.Add(new LoadCommand(top.DeviceId, load.Name, load.Profile.ControlCapabilityId, load.Profile.CurtailedValue, IsRestore: true));
            }
            else // Curtailed -> Normal
            {
                commands.Add(new LoadCommand(top.DeviceId, load.Name, load.Profile.ControlCapabilityId, load.Profile.RestoreValue, IsRestore: true));
            }

            remaining.RemoveAt(remaining.Count - 1);
            popped.Add(top);
            estimated += freed;
        }

        return new RestorePlanResult(commands, popped, estimated);
    }
}

/// <summary>
/// Load-shedding coordinator (roadmap Epic 3C-LM): an event-driven service (reacts to power reports, not
/// a slow tick) that keeps the household's power draw under the budget for the current
/// <c>power_source</c> tier by curtailing/switching off <b>sheddable, non-protected</b> managed loads for
/// the current home mode, and restores them in reverse order once there's headroom again. A soft interlock
/// only — no command gating; off by default (empty/disabled config is a no-op) and it never touches a
/// device whose current-mode tier is "critical" or whose profile is <c>Protected</c> (the safety-floor
/// layering in the roadmap's principle 1).
/// </summary>
public sealed class LoadManager : BackgroundService
{
    // Mirrors DbGateway EnergyEndpoints.MainsRole (Epic 3C) — not shared across the process boundary by
    // convention (see DbGatewayClient.DeviceSnapshot), so the role string is duplicated here.
    private const string MainsRole = "mains";

    // Same convention for the topology vocabulary (DbGateway PowerNodeKinds / PowerPhases, Epic 3C-D).
    private const string SupplyKind = "supply";
    private const string CircuitKind = "circuit";
    private const string ThreePhase = "three";
    private static readonly string[] SinglePhases = { "l1", "l2", "l3" };

    /// <summary>Assumed line voltage when a node states none — turns a breaker rating into watts.</summary>
    private const double DefaultVoltage = 230;

    private static readonly TimeSpan InsufficientNotifyCooldown = TimeSpan.FromMinutes(5);

    private readonly IMessageBus _bus;
    private readonly DeviceRegistry _registry;
    private readonly HomeModeState _mode;
    private readonly PowerSourceState _powerSource;
    private readonly Notifications.NotificationDispatcher _notifications;
    private readonly ILogger<LoadManager> _logger;

    private readonly object _metaGate = new();
    private readonly SemaphoreSlim _evalGate = new(1, 1);
    private readonly Dictionary<Guid, ShedLevel> _levels = new();
    private readonly List<ShedStackEntry> _stack = new();

    private LoadManagementSettings _settings = new();
    private Guid? _mainsDeviceId;
    private IReadOnlyList<Guid> _powerDeviceIds = Array.Empty<Guid>();
    private IReadOnlyDictionary<Guid, LoadCandidate> _loadsById = new Dictionary<Guid, LoadCandidate>();
    private IReadOnlyList<DbGatewayClient.PowerNodeSnapshot> _topology = Array.Empty<DbGatewayClient.PowerNodeSnapshot>();
    private IReadOnlyDictionary<Guid, string> _deviceCircuits = new Dictionary<Guid, string>();
    private DateTime _lastInsufficientNotifyUtc = DateTime.MinValue;

    public LoadManager(
        IMessageBus bus, DeviceRegistry registry, HomeModeState mode, PowerSourceState powerSource,
        Notifications.NotificationDispatcher notifications, ILogger<LoadManager> logger)
    {
        _bus = bus;
        _registry = registry;
        _mode = mode;
        _powerSource = powerSource;
        _notifications = notifications;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _bus.SubscribeAsync<Envelope<DeviceStateReportV1>>(
            "automation-load-manager-state",
            BusTopology.StateExchange,
            BusTopology.DeviceStateUpdatedKey,
            env => HandleStateReport(env, stoppingToken),
            stoppingToken);

        _logger.LogInformation("LoadManager subscribed to device power reports");
    }

    /// <summary>
    /// Refresh tracked device metadata from the read-model (roadmap Epic 3C-LM) — called by
    /// <see cref="RefreshLoop"/> alongside the device/settings refresh it already does each cycle, reusing
    /// the already-fetched device list rather than a second HTTP round-trip. <paramref name="settings"/>
    /// null (gateway unreachable) keeps the last-known config (offline-first).
    /// </summary>
    public void Sync(
        IReadOnlyList<DbGatewayClient.DeviceSnapshot> devices, LoadManagementSettings? settings,
        IReadOnlyList<DbGatewayClient.PowerNodeSnapshot>? topology = null)
    {
        Guid? mains = null;
        var powerIds = new List<Guid>();
        var loads = new Dictionary<Guid, LoadCandidate>();
        var nodes = topology ?? _topology;
        var nodesById = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        foreach (var d in devices)
        {
            if (!Guid.TryParse(d.Id, out var id)) continue;

            if (d.Capabilities.Any(c => c.Id == CapabilityIds.Power) && d.TracksEnergy)
            {
                powerIds.Add(id);
                if (d.EnergyProfile?.Role == MainsRole) mains ??= id;
            }

            if (d.LoadShedding is { Enabled: true } profile)
            {
                var (nominal, curtailed) = EstimateLoadWatts(d, profile);
                var circuitId = d.EnergyProfile?.CircuitId;
                loads[id] = new LoadCandidate(
                    id, d.Name, profile, nominal, curtailed,
                    circuitId, circuitId is not null ? PhaseOf(circuitId, nodesById) : null);
            }
        }

        lock (_metaGate)
        {
            if (settings is not null) _settings = settings;
            if (topology is not null) _topology = topology;
            _mainsDeviceId = mains;
            _powerDeviceIds = powerIds;
            _loadsById = loads;
            _deviceCircuits = devices
                .Where(d => d.EnergyProfile?.CircuitId is not null && Guid.TryParse(d.Id, out _))
                .ToDictionary(d => Guid.Parse(d.Id), d => d.EnergyProfile!.CircuitId!);
        }
    }

    /// <summary>The phase a circuit carries, inherited from the nearest ancestor that states one.</summary>
    private static string? PhaseOf(string nodeId, IReadOnlyDictionary<string, DbGatewayClient.PowerNodeSnapshot> byId)
    {
        var guard = 0;
        var current = byId.GetValueOrDefault(nodeId);
        while (current is not null && guard++ < 32)
        {
            if (!string.IsNullOrWhiteSpace(current.Phase)) return current.Phase;
            current = current.ParentId is { } parentId ? byId.GetValueOrDefault(parentId) : null;
        }
        return null;
    }

    /// <summary>
    /// The watts a managed load costs at normal operation and (when it can be curtailed rather than switched
    /// off) at its curtailed setting — both from the device's energy profile (Epic 3C-D), using the same linear
    /// model the runtime estimates consumption with. A curtail value on a capability that is not this device's
    /// regulator says nothing about power, so the saving stays unknown and the planner turns the load fully off.
    /// </summary>
    private static (double Nominal, double? Curtailed) EstimateLoadWatts(
        DbGatewayClient.DeviceSnapshot device, LoadSheddingProfileSnapshot profile)
    {
        var energy = device.EnergyProfile;
        var nominal = energy?.MaxPowerW ?? 0;
        if (nominal <= 0) return (0, null);

        if (!profile.Curtailable || profile.CurtailedValue is not { } curtailValue) return (nominal, null);

        var scale = EnergyScale.Resolve(energy?.ScaleCapabilityId, device.Capabilities);
        if (scale is null || scale != profile.ControlCapabilityId) return (nominal, null);

        var curtailed = EnergyModel.EstimatePowerW(
            nominal, energy?.MinPowerW ?? 0, energy?.StandbyPowerW ?? 0, isOn: true, scalePercent: curtailValue);
        return (nominal, curtailed);
    }

    private async Task HandleStateReport(Envelope<DeviceStateReportV1> envelope, CancellationToken ct)
    {
        var report = envelope.Data;
        if (report is null || !report.State.ContainsKey(CapabilityIds.Power)) return;

        bool relevant;
        lock (_metaGate) relevant = _powerDeviceIds.Contains(report.DeviceId);
        if (!relevant) return;

        await EvaluateAsync(ct);
    }

    /// <summary>
    /// One thing that can be over its limit: the whole house, one phase, or one circuit (Epic 3C-D). Each
    /// carries what it measures, what it allows and which loads sit inside it, so shedding and restoring use
    /// exactly the same shape whatever tripped.
    /// </summary>
    private sealed record LoadScope(
        string Kind, string? Key, double MeasuredWatts, double LimitWatts, IReadOnlyList<LoadCandidate> Loads);

    /// <summary>Serialized decision pass: measure → shed whatever is over its limit, else restore if there's headroom.</summary>
    private async Task EvaluateAsync(CancellationToken ct)
    {
        if (!await _evalGate.WaitAsync(TimeSpan.FromSeconds(5), ct)) return; // backed up — the next report retries
        try
        {
            LoadManagementSettings settings;
            Guid? mains;
            IReadOnlyList<Guid> powerIds;
            IReadOnlyDictionary<Guid, LoadCandidate> loadsById;
            IReadOnlyList<DbGatewayClient.PowerNodeSnapshot> topology;
            IReadOnlyDictionary<Guid, string> deviceCircuits;
            lock (_metaGate)
            {
                settings = _settings;
                mains = _mainsDeviceId;
                powerIds = _powerDeviceIds;
                loadsById = _loadsById;
                topology = _topology;
                deviceCircuits = _deviceCircuits;
            }
            if (!settings.Enabled) return;

            var mode = _mode.Current;
            var powerSource = _powerSource.Current;
            var now = DateTime.UtcNow;
            var scopes = BuildScopes(settings, powerSource, mains, powerIds, loadsById, topology, deviceCircuits);

            var over = scopes.Where(s => s.MeasuredWatts > s.LimitWatts).ToList();
            if (over.Count > 0)
            {
                // Shed per over-limit scope, tightest first, so a tripped phase is relieved by its own loads
                // rather than by whatever happens to be biggest in the house.
                foreach (var scope in over.OrderBy(s => s.LimitWatts))
                {
                    var candidates = LoadShedPlanner.SheddableCandidates(scope.Loads, mode);
                    var plan = LoadShedPlanner.PlanShed(candidates, _levels, scope.MeasuredWatts, scope.LimitWatts, now);

                    foreach (var cmd in plan.Commands) await PublishCommandAsync(cmd, ct);
                    foreach (var entry in plan.Pushed)
                    {
                        _stack.Add(entry);
                        _levels[entry.DeviceId] = entry.To;
                        await NotifyShedAsync(loadsById, entry, scope.LimitWatts, powerSource, ct);
                    }
                    if (plan.StillOver) await NotifyInsufficientAsync(plan.EstimatedWatts, scope.LimitWatts, now, ct);
                }
                return;
            }

            // Nothing is over: unwind the stack, but only as far as every scope the load belongs to allows.
            var restore = LoadShedPlanner.PlanRestore(
                _stack, loadsById, load => Headroom(load, scopes),
                settings.RestoreMarginWatts, settings.MinDwellSeconds, now);

            foreach (var cmd in restore.Commands) await PublishCommandAsync(cmd, ct);
            foreach (var entry in restore.Popped)
            {
                _stack.Remove(entry);
                if (loadsById.TryGetValue(entry.DeviceId, out var load))
                {
                    _levels[entry.DeviceId] = entry.From;
                    await NotifyRestoreAsync(load, ct);
                }
                else
                {
                    _levels.Remove(entry.DeviceId);
                }
            }
        }
        finally
        {
            _evalGate.Release();
        }
    }

    /// <summary>
    /// Every limit in force right now: the household budget for the live <c>power_source</c>, a scope per phase
    /// that has a limit, and one per rated circuit. Scopes without a limit are left out entirely — an unmapped
    /// house therefore behaves exactly as it did before the topology existed.
    /// </summary>
    private List<LoadScope> BuildScopes(
        LoadManagementSettings settings, string powerSource, Guid? mains, IReadOnlyList<Guid> powerIds,
        IReadOnlyDictionary<Guid, LoadCandidate> loadsById,
        IReadOnlyList<DbGatewayClient.PowerNodeSnapshot> topology,
        IReadOnlyDictionary<Guid, string> deviceCircuits)
    {
        var scopes = new List<LoadScope>();
        var loads = loadsById.Values.ToList();

        var budget = settings.Budgets.FirstOrDefault(b =>
            string.Equals(b.PowerSource, powerSource, StringComparison.OrdinalIgnoreCase));
        if (budget is not null)
            scopes.Add(new LoadScope("house", null, MeasureTotalPowerW(mains, powerIds), budget.LimitWatts, loads));

        if (topology.Count == 0) return scopes;

        var byId = topology.ToDictionary(n => n.Id, StringComparer.Ordinal);

        foreach (var phase in PhaseLimitsInForce(settings, topology, byId))
        {
            var phaseLoads = loads.Where(l => l.Phase == phase.Key || l.Phase == ThreePhase).ToList();
            scopes.Add(new LoadScope(
                "phase", phase.Key, MeasurePhaseWatts(phase.Key, topology, byId, deviceCircuits, powerIds),
                phase.Value, phaseLoads));
        }

        foreach (var node in topology.Where(n => n.Kind == CircuitKind))
        {
            var limit = settings.CircuitLimits.FirstOrDefault(c => c.CircuitId == node.Id)?.LimitWatts
                        ?? BreakerWatts(node);
            if (limit is not { } limitWatts || limitWatts <= 0) continue;

            var circuitLoads = loads.Where(l => l.CircuitId == node.Id).ToList();
            scopes.Add(new LoadScope(
                "circuit", node.Id, MeasureCircuitWatts(node, deviceCircuits, powerIds), limitWatts, circuitLoads));
        }

        return scopes;
    }

    /// <summary>Limit per phase: the explicit setting when present, else the rating of the intake carrying it.</summary>
    private static Dictionary<string, double> PhaseLimitsInForce(
        LoadManagementSettings settings, IReadOnlyList<DbGatewayClient.PowerNodeSnapshot> topology,
        IReadOnlyDictionary<string, DbGatewayClient.PowerNodeSnapshot> byId)
    {
        var limits = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var node in topology.Where(n => n.Kind == SupplyKind))
        {
            if (BreakerWatts(node) is not { } watts) continue;
            var phase = PhaseOf(node.Id, byId);
            var targets = phase == ThreePhase || phase is null ? SinglePhases : new[] { phase };
            foreach (var target in targets)
                if (!limits.TryGetValue(target, out var current) || watts > current) limits[target] = watts;
        }

        foreach (var explicitLimit in settings.PhaseLimits)
            if (explicitLimit.LimitWatts > 0) limits[explicitLimit.Phase] = explicitLimit.LimitWatts;

        return limits;
    }

    /// <summary>Live draw on a phase: its meter when a node on that phase has one, else the sum of the
    /// metered devices sitting on it (a three-phase line contributes a third to each).</summary>
    private double MeasurePhaseWatts(
        string phase, IReadOnlyList<DbGatewayClient.PowerNodeSnapshot> topology,
        IReadOnlyDictionary<string, DbGatewayClient.PowerNodeSnapshot> byId,
        IReadOnlyDictionary<Guid, string> deviceCircuits, IReadOnlyList<Guid> powerIds)
    {
        var metered = topology.FirstOrDefault(n =>
            n.MeterDeviceId is not null && PhaseOf(n.Id, byId) == phase && n.Kind != SupplyKind);
        if (metered?.MeterDeviceId is { } meterId && Guid.TryParse(meterId, out var meterGuid))
            return ToWatts(_registry.GetValue(meterGuid, CapabilityIds.Power));

        double sum = 0;
        foreach (var deviceId in powerIds)
        {
            if (!deviceCircuits.TryGetValue(deviceId, out var circuitId)) continue;
            var devicePhase = PhaseOf(circuitId, byId);
            if (devicePhase == phase) sum += ToWatts(_registry.GetValue(deviceId, CapabilityIds.Power));
            else if (devicePhase == ThreePhase)
                sum += ToWatts(_registry.GetValue(deviceId, CapabilityIds.Power)) / SinglePhases.Length;
        }
        return sum;
    }

    /// <summary>Live draw on a circuit: its own meter when it has one, else the sum of its metered devices.</summary>
    private double MeasureCircuitWatts(
        DbGatewayClient.PowerNodeSnapshot node, IReadOnlyDictionary<Guid, string> deviceCircuits,
        IReadOnlyList<Guid> powerIds)
    {
        if (node.MeterDeviceId is { } meterId && Guid.TryParse(meterId, out var meterGuid))
            return ToWatts(_registry.GetValue(meterGuid, CapabilityIds.Power));

        double sum = 0;
        foreach (var deviceId in powerIds)
            if (deviceCircuits.TryGetValue(deviceId, out var circuitId) && circuitId == node.Id)
                sum += ToWatts(_registry.GetValue(deviceId, CapabilityIds.Power));
        return sum;
    }

    /// <summary>
    /// The measurement/limit pair a restore of this load must respect: the <b>tightest</b> headroom among the
    /// scopes it belongs to. A load that belongs to no limited scope restores freely.
    /// </summary>
    private static (double Measured, double Limit) Headroom(LoadCandidate load, IReadOnlyList<LoadScope> scopes)
    {
        var mine = scopes.Where(s => s.Loads.Any(l => l.DeviceId == load.DeviceId)).ToList();
        if (mine.Count == 0) return (0, double.PositiveInfinity);

        var tightest = mine.OrderBy(s => s.LimitWatts - s.MeasuredWatts).First();
        return (tightest.MeasuredWatts, tightest.LimitWatts);
    }

    /// <summary>The node's breaker rating in watts (A × V), or null when it is unrated.</summary>
    private static double? BreakerWatts(DbGatewayClient.PowerNodeSnapshot node) =>
        node.BreakerAmps is { } amps and > 0 ? amps * (node.Voltage is { } v and > 0 ? v : DefaultVoltage) : null;

    private double MeasureTotalPowerW(Guid? mains, IReadOnlyList<Guid> powerIds)
    {
        if (mains is { } m) return ToWatts(_registry.GetValue(m, CapabilityIds.Power));

        double sum = 0;
        foreach (var id in powerIds) sum += ToWatts(_registry.GetValue(id, CapabilityIds.Power));
        return sum;
    }

    private static double ToWatts(object? v) => ValueOps.Normalize(v) switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        _ => 0,
    };

    private async Task PublishCommandAsync(LoadCommand cmd, CancellationToken ct)
    {
        var envelope = Envelope<DeviceCommandV1>.Create(
            MessageTypes.DeviceCommand,
            source: "system:load-manager",
            data: new DeviceCommandV1(cmd.DeviceId, new Dictionary<string, object?> { [cmd.CapabilityId] = cmd.Value }),
            subject: cmd.DeviceId.ToString());
        await _bus.PublishAsync(BusTopology.CommandsExchange, BusTopology.DeviceCommandKey, envelope, ct);

        _logger.LogInformation("LoadManager {Action} {Device}.{Capability} = {Value}",
            cmd.IsRestore ? "restore" : "shed", cmd.Name, cmd.CapabilityId, cmd.Value);
    }

    private Task NotifyShedAsync(
        IReadOnlyDictionary<Guid, LoadCandidate> loadsById, ShedStackEntry entry,
        double limitWatts, string powerSource, CancellationToken ct)
    {
        var name = loadsById.TryGetValue(entry.DeviceId, out var l) ? l.Name : entry.DeviceId.ToString();
        var action = entry.To == ShedLevel.Curtailed ? "снижена мощность" : "отключено";
        return _notifications.DispatchAsync(new Notifications.NotificationMessage(
            "Управление нагрузкой",
            $"{name}: {action} — превышен бюджет {limitWatts:0} Вт ({powerSource})",
            "warning"), ct);
    }

    private Task NotifyRestoreAsync(LoadCandidate load, CancellationToken ct) =>
        _notifications.DispatchAsync(new Notifications.NotificationMessage(
            "Управление нагрузкой", $"{load.Name}: восстановлено", "info"), ct);

    private Task NotifyInsufficientAsync(double estimatedWatts, double limitWatts, DateTime nowUtc, CancellationToken ct)
    {
        if (nowUtc - _lastInsufficientNotifyUtc < InsufficientNotifyCooldown) return Task.CompletedTask;
        _lastInsufficientNotifyUtc = nowUtc;
        return _notifications.DispatchAsync(new Notifications.NotificationMessage(
            "Управление нагрузкой",
            $"Не хватает управляемых нагрузок, чтобы уложиться в бюджет {limitWatts:0} Вт (осталось {estimatedWatts:0} Вт)",
            "warning"), ct);
    }
}
