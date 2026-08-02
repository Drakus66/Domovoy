// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services.Notifications;
using Domovoy.Contracts.Notifications;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline unit tests for the Epic 3F notification-discipline decision (<see cref="NotificationPolicy"/>):
/// per-category channel routing (opt-out mute), the safety floor, and rate-limit/dedup. Pure — no bus, no DB —
/// exactly so the whole discipline is exercised deterministically (mirrors <c>LoadShedPlanner</c>).
/// </summary>
public sealed class NotificationPolicyTests
{
    private static readonly NotificationPolicy.ChannelInfo Lan = new("lan", Prominent: false);
    private static readonly NotificationPolicy.ChannelInfo Ntfy = new("ntfy", Prominent: true);

    private static NotificationRuntimeState Settings(NotificationSettings s)
    {
        var state = new NotificationRuntimeState();
        state.Set(s);
        return state;
    }

    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Default_DeliversToEveryEnabledChannel()
    {
        var state = Settings(new NotificationSettings());
        var last = new Dictionary<string, DateTimeOffset>();

        var d = NotificationPolicy.Decide(
            new NotificationMessage("t", "b", NotificationSeverities.Info, NotificationCategories.Reactive),
            new[] { Lan, Ntfy }, state, T0, last);

        Assert.False(d.Suppressed);
        Assert.Equal(new[] { "lan", "ntfy" }, d.Channels);
    }

    [Fact]
    public void MutedChannel_ForCategory_IsExcluded()
    {
        var state = Settings(new NotificationSettings
        {
            MutedChannels = new() { [NotificationCategories.Optimization] = new() { "ntfy" } },
        });
        var last = new Dictionary<string, DateTimeOffset>();

        var d = NotificationPolicy.Decide(
            new NotificationMessage("t", "b", NotificationSeverities.Info, NotificationCategories.Optimization),
            new[] { Lan, Ntfy }, state, T0, last);

        Assert.Equal(new[] { "lan" }, d.Channels);
    }

    [Fact]
    public void Safety_IgnoresMute_AndIsForcedOntoProminent()
    {
        // The user muted BOTH channels for reactive — a critical alert must still reach the prominent one.
        var state = Settings(new NotificationSettings
        {
            MutedChannels = new() { [NotificationCategories.Reactive] = new() { "lan", "ntfy" } },
            SafetyFloorEnabled = true,
        });
        var last = new Dictionary<string, DateTimeOffset>();

        var d = NotificationPolicy.Decide(
            new NotificationMessage("leak", "wet", NotificationSeverities.Critical, NotificationCategories.Reactive),
            new[] { Lan, Ntfy }, state, T0, last);

        Assert.False(d.Suppressed);
        Assert.Contains("ntfy", d.Channels);
    }

    [Fact]
    public void Safety_WithOnlyQuietChannels_DeliversButFlagsIt()
    {
        var state = Settings(new NotificationSettings());
        var last = new Dictionary<string, DateTimeOffset>();

        var d = NotificationPolicy.Decide(
            new NotificationMessage("leak", "wet", NotificationSeverities.Critical, NotificationCategories.Reactive),
            new[] { Lan }, state, T0, last); // only the quiet LAN banner exists

        Assert.Equal(new[] { "lan" }, d.Channels);
        Assert.Equal("safety_quiet_only", d.Reason);
    }

    [Fact]
    public void Duplicate_WithinWindow_IsSuppressed_ThenDeliveredAfterIt()
    {
        var state = Settings(new NotificationSettings
        {
            MinIntervalSeconds = new() { [NotificationCategories.Reactive] = 60 },
        });
        var last = new Dictionary<string, DateTimeOffset>();
        var msg = new NotificationMessage("humidity", "high", NotificationSeverities.Warning, NotificationCategories.Reactive);

        var first = NotificationPolicy.Decide(msg, new[] { Lan }, state, T0, last);
        Assert.False(first.Suppressed);

        var repeat = NotificationPolicy.Decide(msg, new[] { Lan }, state, T0.AddSeconds(30), last);
        Assert.True(repeat.Suppressed); // same message, inside the 60 s window → dropped (cry-wolf guard)

        var later = NotificationPolicy.Decide(msg, new[] { Lan }, state, T0.AddSeconds(90), last);
        Assert.False(later.Suppressed); // window elapsed → delivered again
    }

    [Fact]
    public void DifferentMessage_IsNotDeduped()
    {
        var state = Settings(new NotificationSettings
        {
            MinIntervalSeconds = new() { [NotificationCategories.Reactive] = 60 },
        });
        var last = new Dictionary<string, DateTimeOffset>();

        NotificationPolicy.Decide(
            new NotificationMessage("a", "1", NotificationSeverities.Info, NotificationCategories.Reactive),
            new[] { Lan }, state, T0, last);
        var other = NotificationPolicy.Decide(
            new NotificationMessage("b", "2", NotificationSeverities.Info, NotificationCategories.Reactive),
            new[] { Lan }, state, T0.AddSeconds(1), last);

        Assert.False(other.Suppressed);
    }

    [Fact]
    public void Safety_IsNeverRateLimited()
    {
        var state = Settings(new NotificationSettings
        {
            MinIntervalSeconds = new() { [NotificationCategories.Reactive] = 3600 },
        });
        var last = new Dictionary<string, DateTimeOffset>();
        var msg = new NotificationMessage("smoke", "!", NotificationSeverities.Critical, NotificationCategories.Reactive,
            DedupKey: "smoke");

        var first = NotificationPolicy.Decide(msg, new[] { Lan, Ntfy }, state, T0, last);
        var repeat = NotificationPolicy.Decide(msg, new[] { Lan, Ntfy }, state, T0.AddSeconds(5), last);

        Assert.False(first.Suppressed);
        Assert.False(repeat.Suppressed); // safety repeat still lands
    }

    [Fact]
    public void ExplicitDedupKey_CollapsesDifferentBodies()
    {
        var state = Settings(new NotificationSettings
        {
            MinIntervalSeconds = new() { [NotificationCategories.Proactive] = 900 },
        });
        var last = new Dictionary<string, DateTimeOffset>();

        NotificationPolicy.Decide(
            new NotificationMessage("Domovoy", "Found 1 pattern", NotificationSeverities.Info, NotificationCategories.Proactive, DedupKey: "discovery"),
            new[] { Ntfy }, state, T0, last);
        var second = NotificationPolicy.Decide(
            new NotificationMessage("Domovoy", "Found 2 patterns", NotificationSeverities.Info, NotificationCategories.Proactive, DedupKey: "discovery"),
            new[] { Ntfy }, state, T0.AddSeconds(60), last);

        Assert.True(second.Suppressed); // same dedup key, different body → still collapsed
    }
}
