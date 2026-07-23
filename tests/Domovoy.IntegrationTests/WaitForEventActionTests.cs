// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;

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
/// Unit tests for the Epic 3E action branches driven through <see cref="ActionExecutor.ExecuteAsync"/> with
/// a recording fake bus: <c>WaitForEvent</c> (already-holds, event arrives via the broker, or times out into
/// the <c>OnTimeout</c> branch — a terminal branch that replaces the rest of the sequence) and the
/// per-action <c>OnError</c> branch (including the <c>{error}</c> token substituted into a Notify message).
/// </summary>
public sealed class WaitForEventActionTests
{
    private static readonly Guid Door = Guid.NewGuid();
    private static readonly Guid Lamp = Guid.NewGuid();

    private sealed class Harness
    {
        public RecordingBus Bus { get; } = new();
        public DeviceRegistry Registry { get; } = new();
        public DeviceEventBroker Broker { get; } = new();
        public ActionExecutor Executor { get; }

        public Harness()
        {
            var dispatcher = new NotificationDispatcher(
                new INotificationChannel[] { Bus.Notifications }, NullLogger<NotificationDispatcher>.Instance);
            var scenes = new SceneStore(
                new DbGatewayClient(new HttpClient(), NullLogger<DbGatewayClient>.Instance),
                NullLogger<SceneStore>.Instance);
            Executor = new ActionExecutor(Bus, dispatcher, scenes, Registry, Broker,
                Options.Create(new AutomationOptions()), NullLogger<ActionExecutor>.Instance);
        }
    }

    private static AutomationRule Rule(params RuleAction[] actions) => new()
    {
        Id = Guid.NewGuid().ToString(), Name = "wait-rule", Status = RuleStatus.Active, Actions = actions.ToList(),
    };

    private static RuleAction Command(Guid device, string cap, object? value) => new()
    {
        Type = ActionType.Command, DeviceId = device.ToString(),
        Set = new Dictionary<string, object?> { [cap] = value },
    };

    [Fact]
    public async Task WaitForEvent_AlreadyMatches_FallsThroughImmediately()
    {
        var h = new Harness();
        h.Registry.SetValue(Door, "contact", true); // door already open before the run

        var wait = new RuleAction
        {
            Type = ActionType.WaitForEvent, WaitDeviceId = Door.ToString(),
            WaitCapabilityId = "contact", WaitOperator = "eq", WaitValue = true, TimeoutSeconds = 5,
        };
        var rule = Rule(wait, Command(Lamp, "on_off", true));

        await h.Executor.ExecuteAsync(rule, "trigger", conditionsMet: true, CancellationToken.None);

        Assert.Equal(1, h.Bus.CommandCount); // main sequence continued: the lamp command ran
    }

    [Fact]
    public async Task WaitForEvent_EventArrivesViaBroker_FallsThrough()
    {
        var h = new Harness();
        var wait = new RuleAction
        {
            Type = ActionType.WaitForEvent, WaitDeviceId = Door.ToString(),
            WaitCapabilityId = "contact", WaitOperator = "eq", WaitValue = true, TimeoutSeconds = 30,
        };
        var rule = Rule(wait, Command(Lamp, "on_off", true));

        var run = h.Executor.ExecuteAsync(rule, "trigger", conditionsMet: true, CancellationToken.None);
        // Not matched yet — deliver the matching change through the in-process broker (what AutomationEngine does).
        await Task.Delay(50);
        h.Broker.Publish(Door, "contact", true);

        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, h.Bus.CommandCount);
    }

    [Fact]
    public async Task WaitForEvent_Timeout_RunsOnTimeoutBranch_AndStops()
    {
        var h = new Harness();
        var wait = new RuleAction
        {
            Type = ActionType.WaitForEvent, WaitDeviceId = Door.ToString(),
            WaitCapabilityId = "contact", WaitOperator = "eq", WaitValue = true,
            TimeoutSeconds = 1, // times out fast; nothing ever publishes a match
            OnTimeout = new() { Command(Lamp, "alarm", true) },
        };
        // A command AFTER the wait must NOT run once the timeout branch is taken (branch is terminal).
        var rule = Rule(wait, Command(Lamp, "on_off", true));

        await h.Executor.ExecuteAsync(rule, "trigger", conditionsMet: true, CancellationToken.None);

        Assert.Equal(1, h.Bus.CommandCount); // only the OnTimeout branch's command
        Assert.Equal("alarm", h.Bus.LastCommandCapability);
    }

    [Fact]
    public async Task OnError_Branch_RunsWithErrorContext_Substituted()
    {
        var h = new Harness();
        // A Notify whose message throws is hard to force; instead force the failure with a Scene action that
        // references a missing scene? That doesn't throw. Use a Command with a deviceId that parses but a bus
        // that throws for a marked capability — simplest: a custom action the bus rejects.
        var failing = new RuleAction
        {
            Type = ActionType.Command, DeviceId = Lamp.ToString(),
            Set = new Dictionary<string, object?> { [RecordingBus.ThrowCapability] = true },
            OnError = new()
            {
                new RuleAction { Type = ActionType.Notify, Message = "device failed: {error}" },
            },
        };
        var rule = Rule(failing, Command(Lamp, "on_off", true));

        await h.Executor.ExecuteAsync(rule, "trigger", conditionsMet: true, CancellationToken.None);

        Assert.Single(h.Bus.Notifications.Messages);
        Assert.Contains(RecordingBus.ThrowMessage, h.Bus.Notifications.Messages[0]); // {error} was substituted
        Assert.DoesNotContain("{error}", h.Bus.Notifications.Messages[0]);
        Assert.Equal(0, h.Bus.CommandCount); // the trailing command did NOT run (error branch is terminal)
    }

    [Fact]
    public async Task ActionWithoutOnError_Failure_AbortsRun()
    {
        var h = new Harness();
        var failing = new RuleAction
        {
            Type = ActionType.Command, DeviceId = Lamp.ToString(),
            Set = new Dictionary<string, object?> { [RecordingBus.ThrowCapability] = true },
        };
        var rule = Rule(failing, Command(Lamp, "on_off", true));

        // No OnError: the exception aborts the run (caught by ExecuteAsync); the trailing command never runs.
        await h.Executor.ExecuteAsync(rule, "trigger", conditionsMet: true, CancellationToken.None);
        Assert.Equal(0, h.Bus.CommandCount);
    }

    /// <summary>Bus that counts device commands and can be told to throw for one marked capability, so an
    /// action failure (for the OnError tests) is deterministic without any external dependency.</summary>
    private sealed class RecordingBus : IMessageBus
    {
        public const string ThrowCapability = "__throw__";
        public const string ThrowMessage = "bus rejected the command";

        public int CommandCount { get; private set; }
        public string? LastCommandCapability { get; private set; }
        public RecordingChannel Notifications { get; } = new();

        public Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken ct = default)
        {
            if (exchange == BusTopology.CommandsExchange && message is Envelope<DeviceCommandV1> { Data: { } cmd })
            {
                if (cmd.Set.ContainsKey(ThrowCapability)) throw new InvalidOperationException(ThrowMessage);
                CommandCount++;
                LastCommandCapability = cmd.Set.Keys.FirstOrDefault();
            }
            return Task.CompletedTask;
        }

        public Task SubscribeAsync<T>(string queue, string exchange, string routingKey, Func<T, Task> handler, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task UnsubscribeAsync(string queue, CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>Notification channel that records dispatched message bodies (for {error}-substitution asserts).</summary>
    private sealed class RecordingChannel : INotificationChannel
    {
        public ConcurrentQueue<string> Sent { get; } = new();
        public List<string> Messages => Sent.ToList();

        public string Name => "recording";
        public bool Enabled => true;

        public Task<bool> SendAsync(NotificationMessage message, CancellationToken ct)
        {
            Sent.Enqueue(message.Body);
            return Task.FromResult(true);
        }
    }
}
