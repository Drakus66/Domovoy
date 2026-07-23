// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services;
using Domovoy.AutomationService.Services.Discovery;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Intervention mining / dead-rule detection (Epic 3J "living rules"): a rule the household overrides in most of
/// its firings is a candidate to retire, while a rarely-overridden rule, a barely-fired rule, and a late user
/// touch (outside the window) are not. Pure/offline — the miner is a static function over event-log rows.
/// </summary>
public sealed class InterventionMinerTests
{
    private static readonly DateTime Base = new(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);

    private static DbGatewayClient.EventLogEntry Rule(int hour, string dev, string cap, string ruleId) =>
        new() { Timestamp = Base.AddHours(hour), DeviceId = dev, CapabilityId = cap, TriggerSource = "rule", TriggerId = ruleId };

    private static DbGatewayClient.EventLogEntry User(int hour, int plusSeconds, string dev, string cap) =>
        new() { Timestamp = Base.AddHours(hour).AddSeconds(plusSeconds), DeviceId = dev, CapabilityId = cap, TriggerSource = "user" };

    private static AutomationOptions Options() => new(); // defaults: window 300s, minFirings 5, minRate 0.6

    [Fact]
    public void FlagsRuleOverriddenMostOfTheTime()
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var i = 0; i < 6; i++)
        {
            events.Add(Rule(i, "light1", "on_off", "ruleA"));
            if (i < 5) events.Add(User(i, 30, "light1", "on_off")); // 5 of 6 firings overridden within 30s
        }

        var found = InterventionMiner.Mine(events, Options());

        var dead = Assert.Single(found);
        Assert.Equal("ruleA", dead.RuleId);
        Assert.Equal(6, dead.Firings);
        Assert.Equal(5, dead.Overrides);
        Assert.True(dead.OverrideRate > 0.6);
    }

    [Fact]
    public void IgnoresRarelyOverriddenRule()
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var i = 0; i < 6; i++)
        {
            events.Add(Rule(i, "light1", "on_off", "ruleA"));
            if (i == 0) events.Add(User(i, 30, "light1", "on_off")); // 1 of 6 → 17% < 60%
        }

        Assert.Empty(InterventionMiner.Mine(events, Options()));
    }

    [Fact]
    public void RequiresEnoughFirings()
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var i = 0; i < 4; i++) // 4 < minFirings 5, even fully overridden
        {
            events.Add(Rule(i, "light1", "on_off", "ruleA"));
            events.Add(User(i, 30, "light1", "on_off"));
        }

        Assert.Empty(InterventionMiner.Mine(events, Options()));
    }

    [Fact]
    public void LateUserTouchIsNotAnOverride()
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var i = 0; i < 6; i++)
        {
            events.Add(Rule(i, "light1", "on_off", "ruleA"));
            events.Add(User(i, 600, "light1", "on_off")); // 600s > 300s window → not an override
        }

        Assert.Empty(InterventionMiner.Mine(events, Options()));
    }

    [Fact]
    public void UserTouchOnAnotherDeviceIsNotAnOverride()
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var i = 0; i < 6; i++)
        {
            events.Add(Rule(i, "light1", "on_off", "ruleA"));
            events.Add(User(i, 30, "light2", "on_off")); // different device — coincidental use, not an undo
        }

        Assert.Empty(InterventionMiner.Mine(events, Options()));
    }
}
