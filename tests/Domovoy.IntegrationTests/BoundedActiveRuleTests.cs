// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services;
using Domovoy.AutomationService.Services.Notifications;
using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the BoundedActive rule stage (roadmap Epic 1F staged rollout): a promoted-but-unproven
/// rule executes, but no more often than the cooldown window. Pure — the bus is a counting fake.
/// </summary>
public sealed class BoundedActiveRuleTests
{
    [Fact]
    public async Task BoundedActive_Throttles_SecondRunWithinCooldown()
    {
        var bus = new CountingBus();
        var executor = Executor(bus, cooldownSeconds: 300);
        var rule = CommandRule(RuleStatus.BoundedActive);

        await executor.ExecuteAsync(rule, "trigger", conditionsMet: true, CancellationToken.None);
        Assert.Equal(1, bus.CommandCount); // first run actuates

        await executor.ExecuteAsync(rule, "trigger", conditionsMet: true, CancellationToken.None);
        Assert.Equal(1, bus.CommandCount); // second run within cooldown → throttled, no command
    }

    [Fact]
    public async Task BoundedActive_Runs_WhenCooldownIsZero()
    {
        var bus = new CountingBus();
        var executor = Executor(bus, cooldownSeconds: 0);
        var rule = CommandRule(RuleStatus.BoundedActive);

        await executor.ExecuteAsync(rule, "trigger", conditionsMet: true, CancellationToken.None);
        await executor.ExecuteAsync(rule, "trigger", conditionsMet: true, CancellationToken.None);

        Assert.Equal(2, bus.CommandCount); // no cooldown → both runs actuate
    }

    [Fact]
    public async Task Active_IsNeverThrottled()
    {
        var bus = new CountingBus();
        var executor = Executor(bus, cooldownSeconds: 300);
        var rule = CommandRule(RuleStatus.Active);

        await executor.ExecuteAsync(rule, "trigger", conditionsMet: true, CancellationToken.None);
        await executor.ExecuteAsync(rule, "trigger", conditionsMet: true, CancellationToken.None);

        Assert.Equal(2, bus.CommandCount); // Active runs every time regardless of cooldown
    }

    private static ActionExecutor Executor(IMessageBus bus, int cooldownSeconds)
    {
        var dispatcher = new NotificationDispatcher(
            Array.Empty<INotificationChannel>(), NullLogger<NotificationDispatcher>.Instance);
        var options = Options.Create(new AutomationOptions { BoundedActiveCooldownSeconds = cooldownSeconds });
        // Empty scene store (never refreshed) — these tests use Command actions, not scenes.
        var scenes = new SceneStore(
            new DbGatewayClient(new HttpClient(), NullLogger<DbGatewayClient>.Instance),
            NullLogger<SceneStore>.Instance);
        return new ActionExecutor(bus, dispatcher, scenes, options, NullLogger<ActionExecutor>.Instance);
    }

    private static AutomationRule CommandRule(RuleStatus status) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = "bounded-candidate",
        Status = status,
        Actions =
        {
            new RuleAction
            {
                Type = ActionType.Command,
                DeviceId = Guid.NewGuid().ToString(),
                Set = new Dictionary<string, object?> { ["on_off"] = true },
            },
        },
    };

    /// <summary>Counts published device commands (ignores the always-emitted run-history event).</summary>
    private sealed class CountingBus : IMessageBus
    {
        public int CommandCount { get; private set; }

        public Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken ct = default)
        {
            if (exchange == BusTopology.CommandsExchange) CommandCount++;
            return Task.CompletedTask;
        }

        public Task SubscribeAsync<T>(string queue, string exchange, string routingKey, Func<T, Task> handler, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task UnsubscribeAsync(string queue, CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
