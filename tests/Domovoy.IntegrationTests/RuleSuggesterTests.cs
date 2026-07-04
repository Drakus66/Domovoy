using System.Text.Json;

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the heuristic rule proposer's mining core (roadmap Epic 2C). Pure — exercises
/// <see cref="RuleSuggester.Mine"/> over a synthetic chronological event stream; no infrastructure.
/// </summary>
public sealed class RuleSuggesterTests
{
    private static readonly AutomationOptions Options = new(); // defaults: window 120s, minSupport 3, minConf 0.6

    private static readonly DateTime T0 = new(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);

    private static DbGatewayClient.EventLogEntry Ev(int atSeconds, string dev, string cap, bool value, string source) =>
        new()
        {
            Timestamp = T0.AddSeconds(atSeconds),
            DeviceId = dev,
            CapabilityId = cap,
            NewValue = JsonSerializer.SerializeToElement(value),
            TriggerSource = source,
        };

    // A presence trigger followed shortly by a human light-on, repeated over several days.
    private static List<DbGatewayClient.EventLogEntry> Recurring(int times, int gapSeconds, string action = "user")
    {
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var i = 0; i < times; i++)
        {
            var t = i * 3600; // one occurrence per hour
            events.Add(Ev(t, "sensorA", "presence", true, "device"));
            events.Add(Ev(t + gapSeconds, "lightB", "on_off", true, action));
        }
        return events;
    }

    [Fact]
    public void Mines_RecurringPresenceToLight_AboveThresholds()
    {
        var candidates = RuleSuggester.Mine(Recurring(times: 4, gapSeconds: 30), Options);

        var c = Assert.Single(candidates);
        Assert.Equal("sensorA", c.TriggerDeviceId);
        Assert.Equal("presence", c.TriggerCapability);
        Assert.Equal("lightB", c.ActionDeviceId);
        Assert.Equal(4, c.Support);
        Assert.Equal(1.0, c.Confidence);
    }

    [Fact]
    public void BelowSupport_YieldsNothing()
    {
        // Only 2 co-occurrences < minSupport (3).
        Assert.Empty(RuleSuggester.Mine(Recurring(times: 2, gapSeconds: 30), Options));
    }

    [Fact]
    public void ActionOutsideWindow_IsNotCounted()
    {
        // The light comes on 5 minutes after presence — outside the 120s co-occurrence window.
        Assert.Empty(RuleSuggester.Mine(Recurring(times: 4, gapSeconds: 300), Options));
    }

    [Fact]
    public void NonUserAction_IsExcluded()
    {
        // Actions driven by a rule/ML aren't free human choices — the invariant excludes them as labels.
        Assert.Empty(RuleSuggester.Mine(Recurring(times: 4, gapSeconds: 30, action: "rule"), Options));
    }

    [Fact]
    public void Confidence_IsFractionOfTriggersFollowedByAction()
    {
        // 4 presence triggers, only 3 followed by a light-on within the window → confidence 0.75, support 3.
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var i = 0; i < 4; i++)
        {
            var t = i * 3600;
            events.Add(Ev(t, "sensorA", "presence", true, "device"));
            if (i < 3) events.Add(Ev(t + 30, "lightB", "on_off", true, "user"));
        }

        var c = Assert.Single(RuleSuggester.Mine(events, Options));
        Assert.Equal(3, c.Support);
        Assert.Equal(0.75, c.Confidence);
    }

    [Fact]
    public void DeviceControllingItself_IsIgnored()
    {
        // A device whose own presence is followed by its own on_off shouldn't propose wiring it to itself.
        var events = new List<DbGatewayClient.EventLogEntry>();
        for (var i = 0; i < 4; i++)
        {
            var t = i * 3600;
            events.Add(Ev(t, "deviceX", "presence", true, "device"));
            events.Add(Ev(t + 30, "deviceX", "on_off", true, "user"));
        }

        Assert.Empty(RuleSuggester.Mine(events, Options));
    }
}
