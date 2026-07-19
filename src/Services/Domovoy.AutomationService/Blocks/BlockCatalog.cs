// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

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
            new MlPredictorType(models),                              // Epic 2Q: ML prediction as a source signal

            // Epic 2Q: generalized primitives — small reusable building blocks that compose into any control
            // loop (a thermostat = setpoint → hysteresis, etc.), replacing bespoke domain blocks over time.
            new ComparatorType(),
            new HysteresisType(),
            new WindowType(),
            new LogicType(),
            new SelectType(),
            new LinearMapType(),
            new ClampType(),
            new DeadbandType(),
            new RateLimiterType(),
            new AggregateType(),
            new MinDwellType(),
            new PidType(),

            // Epic 2Q Phase 2: time/state primitives (delays, pulses, edges, latches, counters, windowed filters).
            new OnDelayType(),
            new OffDelayType(),
            new PulseType(),
            new IntervalType(),
            new EdgeType(),
            new LatchType(),
            new CounterType(),
            new SampleHoldType(),
            new DebounceType(),
            new MovingAverageType(),
            new MedianFilterType(),

            // Epic 2Q Phase 3: control ramp + the custom expression (script) block.
            new RampType(),
            new ExpressionType(),

            // Presence → home mode as a user-owned block (replaces the hardcoded 1G PresenceMonitor):
            // household policy lives in the block layer, not in platform code.
            new PresenceModeType(),
        };

        // Epic 2I: ML governors are catalog-driven instances of generic types — a new ML-governed output is a
        // config entry here, not a bespoke class. The flagship `ml_thermostat` (Epic 2B) is the
        // (temperature → temperature_setpoint) setpoint instance; toggle/other governors slot in alongside.
        types.AddRange(MlGovernors(models, o));

        _types = types.ToDictionary(t => t.TypeId, StringComparer.OrdinalIgnoreCase);

        // Epic 1H E2: composite blocks are declarative (DSL) documents, not code — parsed against the primitives
        // above and added as first-class types. A new composite is a config entry (built-in example + options),
        // so it needs no rebuild.
        RegisterComposites(CompositeSpecs(o));
    }

    // Built-in example composites + any authored via config. The canonical loop: raw temperature → EWMA smoothing
    // → hysteresis thermostat, as one line; plus a nested example proving composite-in-composite.
    private static List<CompositeSpec> CompositeSpecs(AutomationOptions o)
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
            new()
            {
                // Epic 1D multi-zone irrigation as a branching composite: one shared rain/soil inhibit fans out to
                // several irrigation_sequencer nodes, each surfaced as its own valve output. Authored in the graph
                // DSL (no |>), proving fan-out + multiple outputs end-to-end through the catalog.
                TypeId = "irrigation_multizone",
                Title = "Multi-zone irrigation",
                Description = "Runs several irrigation zones on independent schedules from one shared rain/soil inhibit.",
                Dsl = """
                    in inhibit
                    z1 = irrigation_sequencer(intervalHours=24, runMinutes=15) <- inhibit
                    z2 = irrigation_sequencer(intervalHours=24, runMinutes=20) <- inhibit
                    z3 = irrigation_sequencer(intervalHours=48, runMinutes=10) <- inhibit
                    out zone1 = z1.on_off
                    out zone2 = z2.on_off
                    out zone3 = z3.on_off
                    """,
            },

            // Epic 2Q templates: recipes built from the generalized primitives, showing that the domain blocks
            // are decomposable (a thermostat = smoothing → hysteresis) and that string options flow through the DSL.
            new()
            {
                TypeId = "smoothed_sensor",
                Title = "Smoothed sensor",
                Description = "Rejects spikes with a median filter, then smooths with an EWMA — a clean signal from a noisy sensor.",
                Dsl = "input(value) |> median_filter(window=5) |> ewma_filter(tau=120)",
            },
            new()
            {
                TypeId = "cooling_relay",
                Title = "Cooling relay (hysteresis)",
                Description = "Generic cooling demand: turns on above the high threshold and off below the low, using the inverted hysteresis primitive.",
                Dsl = "input(temperature) |> hysteresis(high=25, low=24, invert=true)",
            },
            new()
            {
                TypeId = "smart_thermostat",
                Title = "Thermostat (from primitives)",
                Description = "The classic thermostat rebuilt from primitives: EWMA smoothing → hysteresis relay. Bind heat to a boiler/valve.",
                // Epic 2Q parameter passthrough: expose the two hysteresis thresholds and the smoothing time
                // constant on the instance, so this ready-made thermostat is tunable without editing the recipe.
                Dsl = """
                    in temperature
                    f = ewma_filter(tau=300) <- temperature
                    h = hysteresis(high=21.5, low=20.5) <- f.value
                    out heat = h.state
                    param high = h.high default 21.5
                    param low = h.low default 20.5
                    param smoothing = f.tau default 300
                    """,
            },
        };
        if (o.Composites is not null) specs.AddRange(o.Composites);
        return specs;
    }

    // Parse + register composites iteratively (roadmap Epic 1H E2). A composite may reference another composite
    // (nesting), so we can't assume the referenced type exists on the first pass: each round registers every spec
    // whose referenced types now resolve, and repeats while it makes progress. An UnknownBlockTypeException means
    // "defer — maybe a later-registered composite"; any other FormatException is a malformed DSL and is dropped.
    // When a round adds nothing, whatever remains references a truly-unknown type (or forms a cycle) and is dropped
    // — a bad composite is skipped, never fatal to the catalog.
    private void RegisterComposites(List<CompositeSpec> specs)
    {
        var pending = specs
            .Where(c => !string.IsNullOrWhiteSpace(c.TypeId) && !string.IsNullOrWhiteSpace(c.Dsl))
            .ToList();

        bool progressed = true;
        while (progressed && pending.Count > 0)
        {
            progressed = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var c = pending[i];
                try
                {
                    var def = BlockDsl.Parse(c.TypeId, c.Title ?? c.TypeId, c.Description ?? "", c.Dsl!, Get);
                    _types[def.TypeId] = new CompositeBlockType(def, Get);
                    pending.RemoveAt(i);
                    progressed = true;
                }
                catch (UnknownBlockTypeException) { /* defer: a referenced composite may register in a later round */ }
                catch (FormatException) { pending.RemoveAt(i); /* malformed → drop, don't fail the catalog */ }
            }
        }
    }

    /// <summary>
    /// The configured ML governor instances (Epic 2I). Each type's predictor closes over its own ML target
    /// (Epic 2P) — the measured input — so multi-target serving needs no signature change in the governors:
    /// the thermostat asks for temperature models, the switch for on_off models, and so on.
    /// </summary>
    private static IEnumerable<IBlockType> MlGovernors(MlModelService models, AutomationOptions o)
    {
        // Predictors thread the instance's pinned model version (Epic 2C); 0 = latest.
        Func<DateTimeOffset, IReadOnlyList<ModelScope>, int, double?> PredictFor(string target) =>
            (now, chain, version) => models.TryPredict(target, now, chain, version, out var v) ? v : null;
        Func<DateTimeOffset, IReadOnlyList<ModelScope>, int, string?> PredictClassFor(string target) =>
            (now, chain, version) => models.TryPredictClass(target, now, chain, version);

        yield return new MlSetpointGovernorType(
            typeId: "ml_thermostat",
            title: "ML thermostat (setpoint governor)",
            description: "Proposes a learned temperature setpoint to a deterministic thermostat loop, staged Shadow → Bounded → Full under the safety floor (Epic 2B/2I).",
            measuredInput: CapabilityIds.Temperature,
            output: WellKnownCapabilities.TemperatureSetpoint(min: o.SetpointMin, max: o.SetpointMax, step: 0.5),
            floorMin: o.SetpointMin,
            floorMax: o.SetpointMax,
            predict: PredictFor(CapabilityIds.Temperature));

        yield return new MlToggleGovernorType(
            typeId: "ml_switch",
            title: "ML switch (on/off governor)",
            description: "Proposes a learned on/off schedule to a deterministic switch, staged Shadow → Bounded → Full with a probability threshold + anti-chatter dwell (Epic 2I). Use when the trained target is a boolean capability.",
            measuredInput: CapabilityIds.OnOff,
            output: WellKnownCapabilities.OnOff(writable: true),
            predict: PredictFor(CapabilityIds.OnOff));

        const string hvacMode = "hvac_mode";
        yield return new MlSelectorGovernorType(
            typeId: "ml_selector",
            title: "ML mode selector",
            description: "Proposes a learned enum schedule (e.g. an HVAC mode) to a deterministic loop, staged Shadow → Bounded → Full, Bounded limited to adjacent values (Epic 2I). Use when the trained target is an enum capability.",
            measuredInput: hvacMode,
            output: WellKnownCapabilities.Enum(hvacMode, new[] { "off", "eco", "comfort", "boost" }, writable: true),
            predict: PredictClassFor(hvacMode));
    }

    public IReadOnlyCollection<IBlockType> Types => _types.Values;

    public IBlockType? Get(string typeId) =>
        typeId is not null && _types.TryGetValue(typeId, out var t) ? t : null;
}
