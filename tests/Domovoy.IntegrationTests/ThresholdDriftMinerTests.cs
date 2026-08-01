// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services;
using Domovoy.AutomationService.Services.Discovery;
using Domovoy.Contracts.Automations;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Seasonal threshold drift (Epic 3J tail 4): when the household keeps overriding a numeric-trigger rule and the
/// trigger's own sensor reads a value far from the configured threshold at those moments, propose shifting the
/// threshold to the lived-in value. Pure/offline; conservative (needs enough overrides + a real drift).
/// </summary>
public sealed class ThresholdDriftMinerTests
{
    private static readonly DateTime Base = new(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc);

    private static JsonElement Num(double v) => JsonDocument.Parse(v.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement;

    // A rule that turns a light on when illuminance drops below 50.
    private static AutomationRule DarkRule() => new()
    {
        Id = "ruleA",
        Name = "Свет по темноте",
        Status = RuleStatus.Active,
        Triggers = new List<RuleTrigger>
        {
            new() { Type = TriggerType.DeviceState, CapabilityId = "illuminance", Operator = "lt", Value = 50.0 },
        },
    };

    private static DbGatewayClient.EventLogEntry Lux(int minute, double value) => new()
    {
        Timestamp = Base.AddMinutes(minute), CapabilityId = "illuminance", DeviceId = "sensor1",
        Kind = "state_change", TriggerSource = "device", NewValue = Num(value),
    };

    [Fact]
    public void ProposesShift_WhenOverridesClusterAwayFromTheThreshold()
    {
        // Illuminance sits around 80 all evening (autumn: dark comes earlier than the rule's 50 threshold).
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var m = 0; m < 60; m++) events.Add(Lux(m, 80));

        // Five overrides, each with the sensor reading ~80 just before.
        var firings = new List<InterventionMiner.Firing>();
        for (var i = 0; i < 5; i++)
        {
            var at = Base.AddMinutes(10 + i * 5);
            firings.Add(new InterventionMiner.Firing("ruleA", "light1", "on_off", at, true, at.AddSeconds(20)));
        }

        var drifts = ThresholdDriftMiner.Mine(events, new[] { DarkRule() }, firings, new AutomationOptions());

        var d = Assert.Single(drifts);
        Assert.Equal("ruleA", d.RuleId);
        Assert.Equal("illuminance", d.CapabilityId);
        Assert.Equal(50, d.CurrentThreshold);
        Assert.Equal(80, d.SuggestedThreshold);
        Assert.Equal(5, d.Samples);
    }

    [Fact]
    public void NoProposal_WhenTooFewOverrides()
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var m = 0; m < 60; m++) events.Add(Lux(m, 80));

        var firings = new List<InterventionMiner.Firing>();
        for (var i = 0; i < 2; i++) // 2 < DriftMinOverrides (4)
        {
            var at = Base.AddMinutes(10 + i * 5);
            firings.Add(new InterventionMiner.Firing("ruleA", "light1", "on_off", at, true, at.AddSeconds(20)));
        }

        Assert.Empty(ThresholdDriftMiner.Mine(events, new[] { DarkRule() }, firings, new AutomationOptions()));
    }

    [Fact]
    public void NoProposal_WhenReadingsMatchTheThreshold()
    {
        // Sensor reads ~50.4 at override time — within noise of the 50 threshold, so no meaningful drift.
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var m = 0; m < 60; m++) events.Add(Lux(m, 50.4));

        var firings = new List<InterventionMiner.Firing>();
        for (var i = 0; i < 5; i++)
        {
            var at = Base.AddMinutes(10 + i * 5);
            firings.Add(new InterventionMiner.Firing("ruleA", "light1", "on_off", at, true, at.AddSeconds(20)));
        }

        Assert.Empty(ThresholdDriftMiner.Mine(events, new[] { DarkRule() }, firings, new AutomationOptions()));
    }
}
