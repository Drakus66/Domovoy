// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net;

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services.Notifications;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline unit tests for the two notification channels added for the mobile-app / remote-access track: the LAN
/// SignalR channel (2M.2 — publishes NotificationRaisedV1 on the bus) and the ntfy push channel (Epic 2O.4 —
/// POSTs to {server}/{topic} with the right headers). No broker / no ntfy server — a fake bus and a capturing
/// HTTP handler verify what each channel emits.
/// </summary>
public sealed class NotificationChannelsTests
{
    // ---- LAN SignalR channel (2M.2) ----

    [Fact]
    public async Task SignalRChannel_Publishes_NotificationRaised_OnTheEventsExchange()
    {
        var bus = new CapturingBus();
        var channel = new SignalRChannel(bus, Opts(new NotificationOptions()), NullLogger<SignalRChannel>.Instance);

        Assert.Equal("lan", channel.Name);
        Assert.True(channel.Enabled); // on by default

        var ok = await channel.SendAsync(new NotificationMessage("Leak detected", "Kitchen sensor is wet", "critical"), default);

        Assert.True(ok);
        Assert.Equal(1, bus.PublishCount);
        Assert.Equal(BusTopology.EventsExchange, bus.Exchange);
        Assert.Equal(BusTopology.NotificationRaisedKey, bus.RoutingKey);

        var envelope = Assert.IsType<Envelope<NotificationRaisedV1>>(bus.Message);
        Assert.Equal(MessageTypes.NotificationRaised, envelope.Type);
        Assert.Equal("Leak detected", envelope.Data!.Title);
        Assert.Equal("Kitchen sensor is wet", envelope.Data.Body);
        Assert.Equal("critical", envelope.Data.Severity);
    }

    [Fact]
    public void SignalRChannel_Disabled_WhenLanOff()
    {
        var options = new NotificationOptions { Lan = new LanChannelOptions { Enabled = false } };
        var channel = new SignalRChannel(new CapturingBus(), Opts(options), NullLogger<SignalRChannel>.Instance);
        Assert.False(channel.Enabled);
    }

    // ---- ntfy push channel (Epic 2O.4) ----

    [Fact]
    public async Task NtfyChannel_Posts_ToTopic_WithTitlePriorityTagsAndAuth()
    {
        var handler = new CapturingHandler();
        var options = new NotificationOptions
        {
            Ntfy = new NtfyChannelOptions { Enabled = true, ServerUrl = "https://ntfy.test/", Topic = "home", Token = "tok" },
        };
        var channel = new NtfyChannel(new SingleClientFactory(handler), Opts(options), NullLogger<NtfyChannel>.Instance);

        Assert.True(channel.Enabled);
        var ok = await channel.SendAsync(new NotificationMessage("Leak detected", "Kitchen sensor is wet", "critical"), default);

        Assert.True(ok);
        Assert.Equal("https://ntfy.test/home", handler.Uri);       // trailing slash on ServerUrl trimmed
        Assert.Equal("Kitchen sensor is wet", handler.Body);        // message text is the POST body
        Assert.Equal("Leak detected", handler.Title);
        Assert.Equal("5", handler.Priority);                        // critical → max priority
        Assert.Equal("rotating_light", handler.Tags);
        Assert.Equal("Bearer tok", handler.Authorization);
    }

    [Theory]
    [InlineData("critical", "5")]
    [InlineData("warning", "4")]
    [InlineData("info", "3")]
    [InlineData("something-else", "3")]
    public async Task NtfyChannel_MapsSeverity_ToPriority(string severity, string expectedPriority)
    {
        var handler = new CapturingHandler();
        var options = new NotificationOptions
        {
            Ntfy = new NtfyChannelOptions { Enabled = true, ServerUrl = "https://ntfy.test", Topic = "home" },
        };
        var channel = new NtfyChannel(new SingleClientFactory(handler), Opts(options), NullLogger<NtfyChannel>.Instance);

        await channel.SendAsync(new NotificationMessage("t", "b", severity), default);
        Assert.Equal(expectedPriority, handler.Priority);
    }

    [Fact]
    public void NtfyChannel_Disabled_WhenServerOrTopicMissing()
    {
        var enabledNoUrl = new NtfyChannelOptions { Enabled = true, ServerUrl = "", Topic = "home" };
        var enabledNoTopic = new NtfyChannelOptions { Enabled = true, ServerUrl = "https://ntfy.test", Topic = "" };
        var disabled = new NtfyChannelOptions { Enabled = false, ServerUrl = "https://ntfy.test", Topic = "home" };

        Assert.False(Channel(enabledNoUrl).Enabled);
        Assert.False(Channel(enabledNoTopic).Enabled);
        Assert.False(Channel(disabled).Enabled);
        Assert.True(Channel(new NtfyChannelOptions { Enabled = true, ServerUrl = "https://ntfy.test", Topic = "home" }).Enabled);

        NtfyChannel Channel(NtfyChannelOptions ntfy) =>
            new(new SingleClientFactory(new CapturingHandler()), Opts(new NotificationOptions { Ntfy = ntfy }), NullLogger<NtfyChannel>.Instance);
    }

    // ---- helpers ----

    private static IOptions<NotificationOptions> Opts(NotificationOptions o) => Options.Create(o);

    private sealed class CapturingBus : IMessageBus
    {
        public string? Exchange;
        public string? RoutingKey;
        public object? Message;
        public int PublishCount;

        public Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default)
        {
            Exchange = exchange;
            RoutingKey = routingKey;
            Message = message;
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task SubscribeAsync<T>(string queue, string exchange, string routingKey, Func<T, Task> handler, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UnsubscribeAsync(string queue, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Uri;
        public string? Body;
        public string? Title;
        public string? Priority;
        public string? Tags;
        public string? Authorization;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Title = Header(request, "Title");
            Priority = Header(request, "Priority");
            Tags = Header(request, "Tags");
            Authorization = Header(request, "Authorization");
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static string? Header(HttpRequestMessage request, string name)
            => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }
}
