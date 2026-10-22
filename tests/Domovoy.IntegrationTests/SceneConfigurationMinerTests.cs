// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services;
using Domovoy.AutomationService.Services.Discovery;
using Domovoy.Contracts.Scenes;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the scene-configuration miner's pure core (roadmap Epic 2F × 3B). Exercises
/// <see cref="SceneConfigurationMiner.Mine"/> over synthetic event streams: it should find a zone state the user
/// repeatedly arranges by hand and propose it as a scene, attach a daily schedule when the arrangements cluster
/// in time, and reject single-device touches, all-off states, non-user (self-inflicted) changes, and
/// configurations the household already saved as a scene.
/// </summary>
public sealed class SceneConfigurationMinerTests
{
    private static readonly AutomationOptions Options = new(); // minDevices 2, minSupport 4, coWindow 180s
    private static readonly DateTime T0 = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DbGatewayClient.DeviceSnapshot Dev(string id, string zone, params string[] writableCaps) => new()
    {
        Id = id,
        Name = id,
        ZoneId = zone,
        Capabilities = writableCaps
            .Select(c => new DbGatewayClient.CapabilitySnapshot { Id = c, Writable = true })
            .ToList(),
    };

    private static DbGatewayClient.EventLogEntry Ev(
        int atSeconds, string dev, string zone, string cap, object? oldV, object newV, string source) => new()
    {
        Timestamp = T0.AddSeconds(atSeconds),
        DeviceId = dev,
        ZoneId = zone,
        CapabilityId = cap,
        OldValue = oldV is null ? null : JsonSerializer.SerializeToElement(oldV),
        NewValue = JsonSerializer.SerializeToElement(newV),
        TriggerSource = source,
    };

    // One deliberate two-lamp arrangement at `atSeconds` (both turned on by hand within the co-window), cleared
    // an hour later by the devices themselves (non-user → no burst).
    private static IEnumerable<DbGatewayClient.EventLogEntry> Arrangement(int atSeconds, string source = "user") => new[]
    {
        Ev(atSeconds, "lampA", "living", "on_off", false, true, source),
        Ev(atSeconds + 30, "lampB", "living", "on_off", false, true, source),
        Ev(atSeconds + 3600, "lampA", "living", "on_off", true, false, "device"),
        Ev(atSeconds + 3600, "lampB", "living", "on_off", true, false, "device"),
    };

    private static List<DbGatewayClient.DeviceSnapshot> TwoLamps() => new()
    {
        Dev("lampA", "living", "on_off", "brightness"),
        Dev("lampB", "living", "on_off", "brightness"),
    };

    private static List<DbGatewayClient.EventLogEntry> DailyArrangements(int days, int hour = 9, string source = "user")
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var d = 0; d < days; d++)
            events.AddRange(Arrangement(d * 86400 + hour * 3600 + d * 60, source)); // small per-day jitter
        return events.OrderBy(e => e.Timestamp).ToList();
    }

    [Fact]
    public void Finds_RecurringTwoDeviceConfiguration()
    {
        var candidates = SceneConfigurationMiner.Mine(
            DailyArrangements(days: 5), TwoLamps(), new List<Scene>(), Options);

        var c = Assert.Single(candidates);
        Assert.Equal("living", c.ZoneId);
        Assert.Equal(5, c.Support);
        Assert.Equal(2, c.Targets.Count);
        Assert.Contains(c.Targets, t => t.DeviceId == "lampA");
        Assert.Contains(c.Targets, t => t.DeviceId == "lampB");
        Assert.All(c.Targets, t => Assert.Equal(true, t.Set["on_off"]));
    }

    [Fact]
    public void Attaches_DailySchedule_WhenArrangementsClusterInTime()
    {
        // All five arrangements happen around 20:00 → a schedule should be suggested near minute 1200.
        var candidates = SceneConfigurationMiner.Mine(
            DailyArrangements(days: 5, hour: 20), TwoLamps(), new List<Scene>(), Options);

        var c = Assert.Single(candidates);
        Assert.NotNull(c.ScheduleMinute);
        Assert.InRange(c.ScheduleMinute!.Value, 1195, 1210); // ~20:00 with sub-hour jitter
    }

    [Fact]
    public void CapturesNumericValue_WhenUserSetsIt()
    {
        // Same nightly arrangement, but each time the user also dials lampA to 40% brightness.
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var d = 0; d < 5; d++)
        {
            var at = d * 86400 + 20 * 3600;
            events.Add(Ev(at, "lampA", "living", "on_off", false, true, "user"));
            events.Add(Ev(at + 10, "lampA", "living", "brightness", 0, 40, "user"));
            events.Add(Ev(at + 30, "lampB", "living", "on_off", false, true, "user"));
            events.Add(Ev(at + 3600, "lampA", "living", "on_off", true, false, "device"));
            events.Add(Ev(at + 3600, "lampB", "living", "on_off", true, false, "device"));
        }

        var candidates = SceneConfigurationMiner.Mine(
            events.OrderBy(e => e.Timestamp).ToList(), TwoLamps(), new List<Scene>(), Options);

        var c = Assert.Single(candidates);
        var lampA = c.Targets.Single(t => t.DeviceId == "lampA");
        Assert.Equal(true, lampA.Set["on_off"]);
        // Numeric values round-trip as double (mirrors SceneEndpoints.NormalizeJsonValues at the HTTP boundary).
        Assert.Equal(40.0, Convert.ToDouble(lampA.Set["brightness"]));
    }

    [Fact]
    public void Ignores_SingleDeviceArrangement()
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var d = 0; d < 5; d++)
        {
            var at = d * 86400 + 9 * 3600;
            events.Add(Ev(at, "lampA", "living", "on_off", false, true, "user"));
            events.Add(Ev(at + 3600, "lampA", "living", "on_off", true, false, "device"));
        }

        Assert.Empty(SceneConfigurationMiner.Mine(
            events.OrderBy(e => e.Timestamp).ToList(), TwoLamps(), new List<Scene>(), Options));
    }

    [Fact]
    public void Ignores_NonUserArrangements()
    {
        // The same recurring state, but reached by a scene/rule firing — never mine your own output (anti-loop).
        Assert.Empty(SceneConfigurationMiner.Mine(
            DailyArrangements(days: 5, source: "scene"), TwoLamps(), new List<Scene>(), Options));
    }

    [Fact]
    public void SkipsConfiguration_AlreadySavedAsScene()
    {
        var existing = new List<Scene>
        {
            new()
            {
                Id = "s1",
                Name = "Evening",
                Targets =
                {
                    new SceneTarget { DeviceId = "lampA", Set = new() { ["on_off"] = true } },
                    new SceneTarget { DeviceId = "lampB", Set = new() { ["on_off"] = true } },
                },
            },
        };

        Assert.Empty(SceneConfigurationMiner.Mine(DailyArrangements(days: 5), TwoLamps(), existing, Options));
    }

    [Fact]
    public void BelowSupport_ProposesNothing()
    {
        Assert.Empty(SceneConfigurationMiner.Mine(
            DailyArrangements(days: 3), TwoLamps(), new List<Scene>(), Options)); // 3 < minSupport 4
    }
}
