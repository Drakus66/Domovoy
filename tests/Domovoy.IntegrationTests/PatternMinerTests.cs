// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services;
using Domovoy.AutomationService.Services.Discovery;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the pattern-discovery funnel's pure core (roadmap Epic 2F). Exercises
/// <see cref="PatternMiner.Mine"/> over synthetic chronological event streams — the whole Stage 0→3 pipeline
/// (slotting, MI/FDR screening, condition mining, gating) — with no infrastructure. Verifies it finds a boolean
/// driver (presence → light) and a numeric threshold driver ("dark" illuminance → light), and rejects
/// self-wiring, ML/rule-driven labels and unrelated actions.
/// </summary>
public sealed class PatternMinerTests
{
    private static readonly AutomationOptions Options = new(); // slot 300s, minSupport 4, minConf 0.6, minLift 1.5

    private static readonly DateTime T0 = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DbGatewayClient.EventLogEntry Ev(int atSeconds, string dev, string cap, object value, string source) =>
        new()
        {
            Timestamp = T0.AddSeconds(atSeconds),
            DeviceId = dev,
            CapabilityId = cap,
            NewValue = JsonSerializer.SerializeToElement(value),
            TriggerSource = source,
        };

    // Presence becomes active entering a slot and a human turns a light on within it, repeated `times` times an
    // hour apart. `action` lets a test route the label through a non-user source.
    private static List<DbGatewayClient.EventLogEntry> PresenceDrivesLight(
        int times, bool withAction = true, string action = "user", string sensorDev = "sensorA", string lightDev = "lightB")
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var i = 0; i < times; i++)
        {
            var t = i * 3600; // aligns to a slot boundary (3600 / 300 = 12 slots apart)
            events.Add(Ev(t, sensorDev, "presence", true, "device"));
            if (withAction) events.Add(Ev(t + 60, lightDev, "on_off", true, action));
            events.Add(Ev(t + 240, sensorDev, "presence", false, "device")); // clears before the next slot
        }
        return events.OrderBy(e => e.Timestamp).ToList();
    }

    [Fact]
    public void Finds_PresenceDrivesLight_AsBooleanTrigger()
    {
        var patterns = PatternMiner.Mine(PresenceDrivesLight(times: 6), Options);

        var p = Assert.Single(patterns);
        Assert.Equal("sensorA", p.TriggerDeviceId);
        Assert.Equal("presence", p.TriggerCapability);
        Assert.Equal("eq", p.TriggerOperator);
        Assert.Equal(true, p.TriggerValue);
        Assert.Equal("lightB", p.ActionDeviceId);
        Assert.Equal(6, p.Support);
        Assert.True(p.Confidence >= 0.6);
        Assert.True(p.Lift >= Options.DiscoveryMinLift);
    }

    [Fact]
    public void Finds_DarkDrivesLight_AsNumericThreshold()
    {
        // Illuminance cycles dark(5)/dusk(80)/day(600) per slot; a human turns the light on whenever it's dark.
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var k = 0; k < 90; k++)
        {
            var level = k % 3 == 0 ? 5.0 : k % 3 == 1 ? 80.0 : 600.0;
            events.Add(Ev(k * 300, "sensorL", "illuminance", level, "device"));
            if (k % 3 == 0) events.Add(Ev(k * 300 + 60, "lightB", "on_off", true, "user"));
        }
        events = events.OrderBy(e => e.Timestamp).ToList();

        var patterns = PatternMiner.Mine(events, Options);

        var p = Assert.Single(patterns, x => x.TriggerCapability == "illuminance");
        Assert.Equal("sensorL", p.TriggerDeviceId);
        Assert.Equal("lt", p.TriggerOperator); // "dark" = below the lower tertile
        Assert.Equal("lightB", p.ActionDeviceId);
        Assert.True(Convert.ToDouble(p.TriggerValue) is > 5 and < 600);
        Assert.True(p.Confidence >= 0.6);
    }

    [Fact]
    public void Rejects_DeviceWiredToItself()
    {
        // A device whose own presence precedes its own on_off must not be proposed (anti-loop).
        var events = PresenceDrivesLight(times: 6, sensorDev: "deviceX", lightDev: "deviceX");
        Assert.Empty(PatternMiner.Mine(events, Options));
    }

    [Fact]
    public void Rejects_NonUserActions()
    {
        // Rule/ML-driven actions aren't free human choices — they can't be labels (no ML-on-ML, no self-fulfilling).
        Assert.Empty(PatternMiner.Mine(PresenceDrivesLight(times: 6, action: "rule"), Options));
    }

    [Fact]
    public void Rejects_UnrelatedActions()
    {
        // Presence in some slots, light turned on in different slots (30 min later, presence already cleared) —
        // the "active" bin has no accompanying action, so nothing qualifies.
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var i = 0; i < 6; i++)
        {
            var t = i * 3600;
            events.Add(Ev(t, "sensorA", "presence", true, "device"));
            events.Add(Ev(t + 240, "sensorA", "presence", false, "device"));
            events.Add(Ev(t + 1800, "lightB", "on_off", true, "user")); // half an hour later, unrelated slot
        }
        Assert.Empty(PatternMiner.Mine(events.OrderBy(e => e.Timestamp).ToList(), Options));
    }
}
