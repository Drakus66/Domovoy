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
/// Unit tests for the type-B setpoint-preference miner (roadmap Epic 2F): learns the numeric setpoint a person
/// repeatedly dials in within a time-of-day bucket, and ignores scattered or loop-driven settings. Pure.
/// </summary>
public sealed class SetpointPreferenceMinerTests
{
    private static readonly AutomationOptions Options = new(); // SetpointMinSupport 4, MaxStdDev 1.0

    private static DbGatewayClient.EventLogEntry Set(DateTime at, string dev, double value, string source = "user") =>
        new()
        {
            Timestamp = at,
            DeviceId = dev,
            CapabilityId = "temperature_setpoint",
            NewValue = JsonSerializer.SerializeToElement(value),
            TriggerSource = source,
        };

    // A night (bucket 3 = 18:00–24:00) setting, repeated over several days near `value`.
    private static List<DbGatewayClient.EventLogEntry> NightlySetpoint(int days, double value, string dev = "thermo", string source = "user")
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        var start = new DateTime(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc);
        for (var d = 0; d < days; d++)
            events.Add(Set(start.AddDays(d).AddMinutes(d), dev, value + (d % 2 == 0 ? 0.2 : -0.2), source));
        return events;
    }

    [Fact]
    public void Learns_StableNightlySetpoint()
    {
        var prefs = SetpointPreferenceMiner.Mine(NightlySetpoint(days: 6, value: 19.0), Options);

        var p = Assert.Single(prefs);
        Assert.Equal("thermo", p.DeviceId);
        Assert.Equal("temperature_setpoint", p.CapabilityId);
        Assert.Equal(3, p.Bucket);            // evening
        Assert.Equal("18:00", p.FromTime);
        Assert.Equal(19.0, p.Value, 1);       // ~19°
        Assert.Equal(6, p.Support);
    }

    [Fact]
    public void Ignores_ScatteredSettings()
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        var start = new DateTime(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc);
        double[] wild = { 16, 24, 18, 26, 20, 15 }; // std dev far above 1.0
        for (var i = 0; i < wild.Length; i++) events.Add(Set(start.AddDays(i), "thermo", wild[i]));

        Assert.Empty(SetpointPreferenceMiner.Mine(events, Options));
    }

    [Fact]
    public void Ignores_LoopWrites_OnlyUserCounts()
    {
        // Same stable value, but written by the control loop (block) rather than a person → not a preference.
        var prefs = SetpointPreferenceMiner.Mine(NightlySetpoint(days: 6, value: 19.0, source: "block:abc"), Options);
        Assert.Empty(prefs);
    }

    [Fact]
    public void RequiresMinimumSupport()
    {
        var prefs = SetpointPreferenceMiner.Mine(NightlySetpoint(days: 3, value: 19.0), Options); // < min support 4
        Assert.Empty(prefs);
    }
}
