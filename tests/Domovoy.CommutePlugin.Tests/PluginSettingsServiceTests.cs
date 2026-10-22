// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.Contracts.Messaging;
using Domovoy.Contracts.Plugins;
using Domovoy.MessageBus;

using Xunit;

namespace Domovoy.CommutePlugin.Tests;

public class PluginSettingsServiceTests
{
    private static readonly IReadOnlyList<PluginSettingDescriptor> Schema = new List<PluginSettingDescriptor>
    {
        new() { Key = "provider", Kind = PluginSettingKinds.Enum, Default = "simulated" },
        new() { Key = "avgSpeedKmh", Kind = PluginSettingKinds.Number, Default = 45.0 },
    };

    [Fact]
    public async Task Start_seeds_defaults_and_announces_schema()
    {
        var bus = new FakeBus();
        var svc = new PluginSettingsService(bus, "commute-planner", Schema);

        await svc.StartAsync();

        // Defaults are available immediately, before any values are applied.
        Assert.Equal("simulated", svc.Get("provider", "x"));
        Assert.Equal(45.0, svc.Get("avgSpeedKmh", 0.0));

        // It subscribed to its per-plugin applied key BEFORE announcing the schema (so the reply isn't missed).
        Assert.Equal(BusTopology.PluginSettingsAppliedKey("commute-planner"), bus.SubscribedRoutingKey);
        var published = Assert.Single(bus.Published);
        Assert.Equal(BusTopology.PluginSettingsSchemaKey, published.RoutingKey);
        Assert.IsType<Envelope<PluginSettingsSchemaV1>>(published.Message);
    }

    [Fact]
    public async Task Applied_values_update_current_and_fire_changed_coercing_json_elements()
    {
        var bus = new FakeBus();
        var svc = new PluginSettingsService(bus, "commute-planner", Schema);
        await svc.StartAsync();

        var raised = false;
        svc.Changed += _ => raised = true;

        // Values arrive as JsonElement over the wire — mirror that exactly.
        var wire = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["provider"] = "tomtom", ["avgSpeedKmh"] = 60 }))!;
        var envelope = Envelope<PluginSettingsAppliedV1>.Create(
            MessageTypes.PluginSettingsApplied, "plugin-supervisor",
            new PluginSettingsAppliedV1("commute-planner", wire), "commute-planner");

        await bus.DeliverAppliedAsync(envelope);

        Assert.True(raised);
        Assert.Equal("tomtom", svc.Get("provider", ""));
        Assert.Equal(60.0, svc.Get("avgSpeedKmh", 0.0));
    }

    [Fact]
    public async Task Applied_for_a_different_plugin_is_ignored()
    {
        var bus = new FakeBus();
        var svc = new PluginSettingsService(bus, "commute-planner", Schema);
        await svc.StartAsync();

        var envelope = Envelope<PluginSettingsAppliedV1>.Create(
            MessageTypes.PluginSettingsApplied, "plugin-supervisor",
            new PluginSettingsAppliedV1("some-other-plugin", new Dictionary<string, object?> { ["provider"] = "tomtom" }),
            "some-other-plugin");

        await bus.DeliverAppliedAsync(envelope);

        Assert.Equal("simulated", svc.Get("provider", "")); // unchanged
    }

    /// <summary>
    /// Дискриминатор коэрсии — <c>typeof(T)</c>, а не <c>default(T)</c>. Для строки <c>default(T)</c>
    /// равен <c>null</c>, поэтому строковая ветка была недостижима: нестроковое значение, сохранённое для
    /// строковой настройки, уходило в <c>Deserialize&lt;string&gt;()</c>, бросало и молча превращалось в
    /// fallback вместо своего исходного текста.
    /// </summary>
    [Theory]
    [InlineData("\"tomtom\"", "tomtom")]
    [InlineData("42", "42")]
    [InlineData("true", "true")]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    public void Coerce_to_string_keeps_the_raw_text_of_a_non_string_element(string json, string expected) =>
        Assert.Equal(expected, PluginSettingsService.Coerce(JsonDocument.Parse(json).RootElement, "fallback"));

    [Theory]
    [InlineData("60", 60.0)]
    [InlineData("\"60.5\"", 60.5)]
    public void Coerce_to_number_reads_both_forms(string json, double expected) =>
        Assert.Equal(expected, PluginSettingsService.Coerce(JsonDocument.Parse(json).RootElement, 0.0));

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"true\"", true)]
    public void Coerce_to_bool_reads_both_forms(string json, bool expected) =>
        Assert.Equal(expected, PluginSettingsService.Coerce(JsonDocument.Parse(json).RootElement, false));

    /// <summary>Minimal in-memory bus that captures the applied-subscription handler and records publishes.</summary>
    private sealed class FakeBus : IMessageBus
    {
        private Func<Envelope<PluginSettingsAppliedV1>, Task>? _appliedHandler;

        public string? SubscribedRoutingKey { get; private set; }
        public List<(string Exchange, string RoutingKey, object Message)> Published { get; } = new();

        public Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default)
        {
            Published.Add((exchange, routingKey, message!));
            return Task.CompletedTask;
        }

        public Task SubscribeAsync<T>(string queue, string exchange, string routingKey, Func<T, Task> handler, CancellationToken cancellationToken = default)
        {
            SubscribedRoutingKey = routingKey;
            if (handler is Func<Envelope<PluginSettingsAppliedV1>, Task> applied)
                _appliedHandler = applied;
            return Task.CompletedTask;
        }

        public Task DeliverAppliedAsync(Envelope<PluginSettingsAppliedV1> envelope) =>
            _appliedHandler?.Invoke(envelope) ?? Task.CompletedTask;

        public Task UnsubscribeAsync(string queue, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
