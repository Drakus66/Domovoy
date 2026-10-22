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
/// Unit tests for the scene-schedule miner's pure core (roadmap Epic 2F × 3B). Exercises
/// <see cref="SceneActivationMiner.Mine"/>: it should notice an existing scene the user keeps activating around
/// the same time of day (attributed via the <c>scene</c> trigger-source) and suggest a daily schedule, while
/// rejecting too-few or scattered activations, rule-driven activations (already automated), and unknown scenes.
/// </summary>
public sealed class SceneActivationMinerTests
{
    private static readonly AutomationOptions Options = new(); // scheduleMinSupport 4, maxSpread 45 min
    private static readonly DateTime T0 = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly List<Scene> Scenes = new()
    {
        new() { Id = "sceneX", Name = "Evening" },
    };

    private static DbGatewayClient.EventLogEntry Ev(int atSeconds, string cap, string source, string? triggerId) => new()
    {
        Timestamp = T0.AddSeconds(atSeconds),
        DeviceId = "lampA",
        ZoneId = "living",
        CapabilityId = cap,
        NewValue = JsonSerializer.SerializeToElement(true),
        TriggerSource = source,
        TriggerId = triggerId,
    };

    // One scene activation = a fan-out of two device events sharing the scene id within a second or two.
    private static IEnumerable<DbGatewayClient.EventLogEntry> Activation(int atSeconds, string source = "scene", string? id = "sceneX") => new[]
    {
        Ev(atSeconds, "on_off", source, id),
        Ev(atSeconds + 1, "brightness", source, id),
    };

    private static List<DbGatewayClient.EventLogEntry> DailyActivations(int days, int hour, int jitterPerDay = 60, string source = "scene", string? id = "sceneX")
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var d = 0; d < days; d++)
            events.AddRange(Activation(d * 86400 + hour * 3600 + d * jitterPerDay, source, id));
        return events.OrderBy(e => e.Timestamp).ToList();
    }

    [Fact]
    public void Finds_SceneActivatedAtConsistentTime()
    {
        var schedules = SceneActivationMiner.Mine(DailyActivations(days: 5, hour: 20), Scenes, Options);

        var s = Assert.Single(schedules);
        Assert.Equal("sceneX", s.SceneId);
        Assert.Equal("Evening", s.SceneName);
        Assert.Equal(5, s.Support);
        Assert.InRange(s.Minute, 1195, 1210); // ~20:00
    }

    [Fact]
    public void MinuteOfDay_IsSiteWallClock_NotUtc()
    {
        // The household presses the tile at 23:00 local in a UTC+3 site; the event-log stamps 20:00 UTC. The
        // proposal has to say (and the cron has to fire at) 23:00 — the scheduler matches cron on the site clock.
        var plusThree = TimeZoneInfo.CreateCustomTimeZone("Test/Plus3", TimeSpan.FromHours(3), "Test +3", "Test +3");

        var s = Assert.Single(SceneActivationMiner.Mine(
            DailyActivations(days: 5, hour: 20), Scenes, Options, plusThree));

        Assert.InRange(s.Minute, 1375, 1390); // ~23:00 local, not ~20:00 UTC
    }

    [Fact]
    public void BelowSupport_ProposesNothing()
    {
        Assert.Empty(SceneActivationMiner.Mine(DailyActivations(days: 3, hour: 20), Scenes, Options)); // 3 < 4
    }

    [Fact]
    public void ScatteredTimes_ProposeNothing()
    {
        // Five activations spread across the whole day → not a schedulable time of day.
        var events = new List<DbGatewayClient.EventLogEntry>();
        var hours = new[] { 6, 10, 14, 18, 22 };
        for (var d = 0; d < hours.Length; d++)
            events.AddRange(Activation(d * 86400 + hours[d] * 3600));

        Assert.Empty(SceneActivationMiner.Mine(events.OrderBy(e => e.Timestamp).ToList(), Scenes, Options));
    }

    [Fact]
    public void IgnoresRuleDrivenActivations()
    {
        // A rule already activates it (source "rule", not "scene") — nothing to schedule.
        Assert.Empty(SceneActivationMiner.Mine(DailyActivations(days: 5, hour: 20, source: "rule"), Scenes, Options));
    }

    [Fact]
    public void IgnoresUnknownScene()
    {
        Assert.Empty(SceneActivationMiner.Mine(DailyActivations(days: 5, hour: 20, id: "ghost"), Scenes, Options));
    }
}
