// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Capabilities;

using Xunit;

using LoadSheddingProfileSnapshot = Domovoy.AutomationService.Services.DbGatewayClient.LoadSheddingProfileSnapshot;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the pure load-shedding decision core (roadmap Epic 3C-LM) — no bus/DB. Covers candidate
/// filtering (protected/critical/disabled never shed), the curtail-before-off phasing, early stop once
/// under budget, and reverse-order restore with min-dwell + margin (never skipping ahead in the stack).
/// </summary>
public sealed class LoadShedPlannerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Home = "Home";

    private static LoadCandidate Load(
        Guid id, string name, bool enabled = true, bool @protected = false,
        string tier = LoadShedPlanner.TierSheddable, int priority = 0,
        double? nominalW = null, bool curtailable = false, double? curtailedW = null,
        double? curtailedValue = null, double? restoreValue = null, string controlCapabilityId = CapabilityIds.OnOff) =>
        new(id, name, new LoadSheddingProfileSnapshot
        {
            Enabled = enabled,
            Protected = @protected,
            ControlCapabilityId = controlCapabilityId,
            Curtailable = curtailable,
            CurtailedValue = curtailedValue,
            RestoreValue = restoreValue,
            ModeTier = new() { [Home] = tier },
            ModePriority = new() { [Home] = priority },
        }, nominalW ?? 0, curtailedW);

    [Fact]
    public void SheddableCandidates_ExcludesProtectedCriticalAndDisabled()
    {
        var protectedLoad = Load(Guid.NewGuid(), "Protected", @protected: true, priority: 1);
        var criticalLoad = Load(Guid.NewGuid(), "Critical", tier: LoadShedPlanner.TierCritical);
        var disabledLoad = Load(Guid.NewGuid(), "Disabled", enabled: false, priority: 0);
        var sheddable = Load(Guid.NewGuid(), "Sheddable", priority: 2);

        var result = LoadShedPlanner.SheddableCandidates(
            new[] { protectedLoad, criticalLoad, disabledLoad, sheddable }, Home);

        Assert.Equal(new[] { sheddable.DeviceId }, result.Select(l => l.DeviceId));
    }

    [Fact]
    public void SheddableCandidates_OrdersByPriorityThenNominalPowerDescending()
    {
        var low = Load(Guid.NewGuid(), "LowPriorityNumber", priority: 1, nominalW: 50);
        var big = Load(Guid.NewGuid(), "SamePriorityBigger", priority: 1, nominalW: 200);
        var last = Load(Guid.NewGuid(), "HighPriorityNumber", priority: 2, nominalW: 900);

        var result = LoadShedPlanner.SheddableCandidates(new[] { last, low, big }, Home);

        Assert.Equal(new[] { big.DeviceId, low.DeviceId, last.DeviceId }, result.Select(l => l.DeviceId));
    }

    [Fact]
    public void PlanShed_CurtailsBeforeTurningOff_StopsAsSoonAsUnderBudget()
    {
        var load = Load(Guid.NewGuid(), "Heater", priority: 0, nominalW: 1000, curtailable: true, curtailedW: 200, curtailedValue: 30);
        var plan = LoadShedPlanner.PlanShed(new[] { load }, new Dictionary<Guid, ShedLevel>(), measuredWatts: 1000, limitWatts: 500, Now);

        var cmd = Assert.Single(plan.Commands);
        Assert.Equal(load.Profile.ControlCapabilityId, cmd.CapabilityId);
        Assert.Equal(30d, cmd.Value);
        Assert.False(cmd.IsRestore);

        var pushed = Assert.Single(plan.Pushed);
        Assert.Equal(ShedLevel.Normal, pushed.From);
        Assert.Equal(ShedLevel.Curtailed, pushed.To);

        Assert.Equal(200, plan.EstimatedWatts); // 1000 - (1000-200) freed
        Assert.False(plan.StillOver);
    }

    [Fact]
    public void PlanShed_SkipsCurtailPhase_WhenCurtailedPowerUnset()
    {
        var load = Load(Guid.NewGuid(), "Dimmer", priority: 0, nominalW: 500, curtailable: true, curtailedW: null);
        var plan = LoadShedPlanner.PlanShed(new[] { load }, new Dictionary<Guid, ShedLevel>(), measuredWatts: 1000, limitWatts: 500, Now);

        var cmd = Assert.Single(plan.Commands);
        Assert.Equal(CapabilityIds.OnOff, cmd.CapabilityId);
        Assert.Equal(false, cmd.Value);
        Assert.Equal(ShedLevel.Off, Assert.Single(plan.Pushed).To);
    }

    [Fact]
    public void PlanShed_NonCurtailable_TurnsOffDirectly()
    {
        var load = Load(Guid.NewGuid(), "Pump", priority: 0, nominalW: 500);
        var plan = LoadShedPlanner.PlanShed(new[] { load }, new Dictionary<Guid, ShedLevel>(), measuredWatts: 1000, limitWatts: 500, Now);

        Assert.Equal(500, plan.EstimatedWatts);
        Assert.False(plan.StillOver);
        Assert.Equal(ShedLevel.Off, Assert.Single(plan.Pushed).To);
    }

    [Fact]
    public void PlanShed_StopsAtFirstCandidateThatClearsBudget_LeavesLowerPriorityUntouched()
    {
        var first = Load(Guid.NewGuid(), "First", priority: 0, nominalW: 600);
        var second = Load(Guid.NewGuid(), "Second", priority: 1, nominalW: 600);
        var plan = LoadShedPlanner.PlanShed(new[] { first, second }, new Dictionary<Guid, ShedLevel>(), measuredWatts: 1000, limitWatts: 500, Now);

        var pushed = Assert.Single(plan.Pushed);
        Assert.Equal(first.DeviceId, pushed.DeviceId);
        Assert.Equal(400, plan.EstimatedWatts);
    }

    [Fact]
    public void PlanShed_StillOver_WhenCandidatesExhausted()
    {
        var load = Load(Guid.NewGuid(), "Small", priority: 0, nominalW: 100);
        var plan = LoadShedPlanner.PlanShed(new[] { load }, new Dictionary<Guid, ShedLevel>(), measuredWatts: 1000, limitWatts: 500, Now);

        Assert.True(plan.StillOver);
        Assert.Equal(900, plan.EstimatedWatts);
    }

    /// <summary>
    /// Единый бюджет дома в виде, который принимает живая перегрузка <c>PlanRestore</c>. До этого тесты
    /// звали обёртку без scope — единственного её потребителя в продакшн-коде не было, и удаление
    /// мёртвой обёртки не должно было терять покрытие.
    /// </summary>
    private static Func<LoadCandidate, (double Measured, double Limit)> Budget(double measuredWatts, double limitWatts) =>
        _ => (measuredWatts, limitWatts);

    [Fact]
    public void PlanRestore_RespectsMinDwell()
    {
        var id = Guid.NewGuid();
        var load = Load(id, "Recent", nominalW: 300);
        var stack = new[] { new ShedStackEntry(id, ShedLevel.Normal, ShedLevel.Off, Now.AddSeconds(-30)) };
        var loadsById = new Dictionary<Guid, LoadCandidate> { [id] = load };

        var plan = LoadShedPlanner.PlanRestore(stack, loadsById, Budget(100, 1000), marginWatts: 50, minDwellSeconds: 120, Now);

        Assert.Empty(plan.Commands);
        Assert.Empty(plan.Popped);
        Assert.Equal(0, plan.RestoredWatts); // nothing came back
    }

    [Fact]
    public void PlanRestore_PopsStackInReverseOrder_RespectingMargin()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var loadA = Load(idA, "A", nominalW: 300);
        var loadB = Load(idB, "B", nominalW: 200);
        // A shed first (older stack entry), B shed second (top of stack — restored first).
        var stack = new List<ShedStackEntry>
        {
            new(idA, ShedLevel.Normal, ShedLevel.Off, Now.AddMinutes(-10)),
            new(idB, ShedLevel.Normal, ShedLevel.Off, Now.AddMinutes(-5)),
        };
        var loadsById = new Dictionary<Guid, LoadCandidate> { [idA] = loadA, [idB] = loadB };

        var plan = LoadShedPlanner.PlanRestore(stack, loadsById, Budget(100, 1000), marginWatts: 50, minDwellSeconds: 60, Now);

        Assert.Equal(new[] { idB, idA }, plan.Popped.Select(p => p.DeviceId));
        Assert.Equal(500, plan.RestoredWatts); // 200 (B) + 300 (A) given back on top of the measured 100
        Assert.Equal(2, plan.Commands.Count);
    }

    [Fact]
    public void PlanRestore_StopsWhenMarginNotMet_NeverSkipsAhead()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var loadA = Load(idA, "A", nominalW: 100); // would fit
        var loadB = Load(idB, "B", nominalW: 900); // blocks the margin on its own
        var stack = new List<ShedStackEntry>
        {
            new(idA, ShedLevel.Normal, ShedLevel.Off, Now.AddMinutes(-10)),
            new(idB, ShedLevel.Normal, ShedLevel.Off, Now.AddMinutes(-5)), // top of stack
        };
        var loadsById = new Dictionary<Guid, LoadCandidate> { [idA] = loadA, [idB] = loadB };

        var plan = LoadShedPlanner.PlanRestore(stack, loadsById, Budget(100, 1000), marginWatts: 50, minDwellSeconds: 60, Now);

        // B (top) doesn't fit (100+900+50 > 1000) → stop immediately, A (which would fit) is never reached.
        Assert.Empty(plan.Commands);
        Assert.Empty(plan.Popped);
    }

    [Fact]
    public void PlanRestore_OffToCurtailed_TurnsOnThenReappliesCurtailedValue()
    {
        var id = Guid.NewGuid();
        var load = Load(id, "Dimmer", nominalW: 500, curtailable: true, curtailedW: 100,
            curtailedValue: 25, controlCapabilityId: "brightness");
        var stack = new[] { new ShedStackEntry(id, ShedLevel.Curtailed, ShedLevel.Off, Now.AddMinutes(-10)) };
        var loadsById = new Dictionary<Guid, LoadCandidate> { [id] = load };

        var plan = LoadShedPlanner.PlanRestore(stack, loadsById, Budget(0, 1000), marginWatts: 0, minDwellSeconds: 60, Now);

        Assert.Equal(2, plan.Commands.Count);
        Assert.Equal(CapabilityIds.OnOff, plan.Commands[0].CapabilityId);
        Assert.Equal(true, plan.Commands[0].Value);
        Assert.Equal("brightness", plan.Commands[1].CapabilityId);
        Assert.Equal(25d, plan.Commands[1].Value);
    }

    [Fact]
    public void PlanRestore_CurtailedToNormal_CommandsRestoreValue()
    {
        var id = Guid.NewGuid();
        var load = Load(id, "Dimmer", nominalW: 500, curtailable: true, curtailedW: 100,
            restoreValue: 80, controlCapabilityId: "brightness");
        var stack = new[] { new ShedStackEntry(id, ShedLevel.Normal, ShedLevel.Curtailed, Now.AddMinutes(-10)) };
        var loadsById = new Dictionary<Guid, LoadCandidate> { [id] = load };

        var plan = LoadShedPlanner.PlanRestore(stack, loadsById, Budget(0, 1000), marginWatts: 0, minDwellSeconds: 60, Now);

        var cmd = Assert.Single(plan.Commands);
        Assert.Equal("brightness", cmd.CapabilityId);
        Assert.Equal(80d, cmd.Value);
        Assert.True(cmd.IsRestore);
    }

    [Fact]
    public void PlanRestore_DropsStaleEntry_WhenLoadNoLongerConfigured()
    {
        var goneId = Guid.NewGuid();
        var stack = new[] { new ShedStackEntry(goneId, ShedLevel.Normal, ShedLevel.Off, Now.AddMinutes(-10)) };

        var plan = LoadShedPlanner.PlanRestore(
            stack, new Dictionary<Guid, LoadCandidate>(), Budget(0, 1000), marginWatts: 0, minDwellSeconds: 60, Now);

        Assert.Empty(plan.Commands);
        Assert.Equal(goneId, Assert.Single(plan.Popped).DeviceId);
    }

    [Fact]
    public void Tier_And_Priority_DefaultToUnmanagedAndLowest_WhenModeHasNoEntry()
    {
        var profile = new LoadSheddingProfileSnapshot();
        Assert.Equal(LoadShedPlanner.TierUnmanaged, LoadShedPlanner.Tier(profile, "Vacation"));
        Assert.Equal(int.MaxValue, LoadShedPlanner.Priority(profile, "Vacation"));
    }
}
