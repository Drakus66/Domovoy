using Domovoy.AutomationService.Blocks.Composite;
using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Ml;
using Domovoy.AutomationService.Ml.Governors;
using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// Registry of built-in (E1) block types (roadmap Epic 1H). New first-party types register here; new
/// <i>instances</i> are pure config. The catalog also drives the UI's typed authoring form (the schema
/// of ports/params/outputs is served via <c>GET /api/blocks/catalog</c>).
/// </summary>
public sealed class BlockCatalog
{
    private readonly Dictionary<string, IBlockType> _types;

    public BlockCatalog(MlModelService models, SunCalculator sun, IOptions<AutomationOptions> options)
    {
        var o = options.Value;
        var types = new List<IBlockType>
        {
            new EwmaFilterType(),
            new ThermostatType(),
            new Co2VentilationType(),
            new IrrigationSequencerType(),
            new SunGateType(sun),                                     // Epic 1D: outdoor lighting by sun
            new MlSetpointType(models, o.SetpointMin, o.SetpointMax), // Epic 2A: ML-driven setpoint
        };

        // Epic 2I: ML governors are catalog-driven instances of generic types — a new ML-governed output is a
        // config entry here, not a bespoke class. The flagship `ml_thermostat` (Epic 2B) is the
        // (temperature → temperature_setpoint) setpoint instance; toggle/other governors slot in alongside.
        types.AddRange(MlGovernors(models, o));

        _types = types.ToDictionary(t => t.TypeId, StringComparer.OrdinalIgnoreCase);

        // Epic 1H E2: composite blocks are declarative (DSL) documents, not code — parsed against the primitives
        // above and added as first-class types. A new composite is a config entry (built-in example + options),
        // so it needs no rebuild. Composites reference primitives only in v1, so the dict is complete here.
        foreach (var composite in CompositeDefinitions(o))
            _types[composite.TypeId] = new CompositeBlockType(composite, Get);
    }

    // Built-in example composite + any authored via config, each parsed defensively (a malformed one is skipped,
    // not fatal). The canonical loop: raw temperature → EWMA smoothing → hysteresis thermostat, as one line.
    private List<CompositeDefinition> CompositeDefinitions(AutomationOptions o)
    {
        var specs = new List<CompositeSpec>
        {
            new()
            {
                TypeId = "climate_loop",
                Title = "Climate loop (EWMA → thermostat)",
                Description = "Smooths a temperature with an EWMA filter, then drives a hysteresis thermostat — the canonical filter→controller composite.",
                Dsl = "input(temperature) |> ewma_filter(tau=300) |> thermostat(setpoint=21, hysteresis=0.5)",
            },
        };
        if (o.Composites is not null) specs.AddRange(o.Composites);

        var defs = new List<CompositeDefinition>();
        foreach (var c in specs)
        {
            if (string.IsNullOrWhiteSpace(c.TypeId) || string.IsNullOrWhiteSpace(c.Dsl)) continue;
            try { defs.Add(BlockDsl.Parse(c.TypeId, c.Title ?? c.TypeId, c.Description ?? "", c.Dsl!, Get)); }
            catch (FormatException) { /* skip a malformed composite rather than fail the whole catalog */ }
        }
        return defs;
    }

    /// <summary>The configured ML governor instances (Epic 2I). They share one predictor over the loaded model.</summary>
    private static IEnumerable<IBlockType> MlGovernors(MlModelService models, AutomationOptions o)
    {
        // Predictors thread the instance's pinned model version (Epic 2C); 0 = latest.
        Func<DateTimeOffset, IReadOnlyList<ModelScope>, int, double?> predict =
            (now, chain, version) => models.TryPredict(now, chain, version, out var v) ? v : null;
        Func<DateTimeOffset, IReadOnlyList<ModelScope>, int, string?> predictClass =
            (now, chain, version) => models.TryPredictClass(now, chain, version);

        yield return new MlSetpointGovernorType(
            typeId: "ml_thermostat",
            title: "ML thermostat (setpoint governor)",
            description: "Proposes a learned temperature setpoint to a deterministic thermostat loop, staged Shadow → Bounded → Full under the safety floor (Epic 2B/2I).",
            measuredInput: CapabilityIds.Temperature,
            output: WellKnownCapabilities.TemperatureSetpoint(min: o.SetpointMin, max: o.SetpointMax, step: 0.5),
            floorMin: o.SetpointMin,
            floorMax: o.SetpointMax,
            predict: predict);

        yield return new MlToggleGovernorType(
            typeId: "ml_switch",
            title: "ML switch (on/off governor)",
            description: "Proposes a learned on/off schedule to a deterministic switch, staged Shadow → Bounded → Full with a probability threshold + anti-chatter dwell (Epic 2I). Use when the trained target is a boolean capability.",
            measuredInput: CapabilityIds.OnOff,
            output: WellKnownCapabilities.OnOff(writable: true),
            predict: predict);

        const string hvacMode = "hvac_mode";
        yield return new MlSelectorGovernorType(
            typeId: "ml_selector",
            title: "ML mode selector",
            description: "Proposes a learned enum schedule (e.g. an HVAC mode) to a deterministic loop, staged Shadow → Bounded → Full, Bounded limited to adjacent values (Epic 2I). Use when the trained target is an enum capability.",
            measuredInput: hvacMode,
            output: WellKnownCapabilities.Enum(hvacMode, new[] { "off", "eco", "comfort", "boost" }, writable: true),
            predict: predictClass);
    }

    public IReadOnlyCollection<IBlockType> Types => _types.Values;

    public IBlockType? Get(string typeId) =>
        typeId is not null && _types.TryGetValue(typeId, out var t) ? t : null;
}
