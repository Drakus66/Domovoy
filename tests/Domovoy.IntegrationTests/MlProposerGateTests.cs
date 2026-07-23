// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net;
using System.Text;

using Domovoy.AutomationService.Ml;
using Domovoy.AutomationService.Services;
using Domovoy.AutomationService.Services.Notifications;
using Domovoy.Contracts.Ml;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// The "silence and pulse" gate (Epic 3I): the shared policy every proactive proposer runs before a periodic
/// scan. Verifies the master switch and the proposers switch short-circuit before any work, and the cold-start
/// history gate holds the proposers back until the event-log is old enough — recording that wait as progress
/// (but not the persistent off-states). The earliest-event read is stubbed; no Mongo needed.
/// </summary>
public sealed class MlProposerGateTests
{
    // Stubs GET api/events/earliest with a fixed timestamp (or an empty log when null).
    private sealed class EarliestStub : HttpMessageHandler
    {
        private readonly DateTime? _earliest;
        public EarliestStub(DateTime? earliest) => _earliest = earliest;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = _earliest is { } e
                ? $"{{\"earliest\":\"{e:o}\"}}"
                : "{\"earliest\":null}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static MlProposerGate Build(MlSettings settings, DateTime? earliest)
    {
        var runtime = new MlRuntimeState();
        runtime.Set(settings);
        var db = new DbGatewayClient(
            new HttpClient(new EarliestStub(earliest)) { BaseAddress = new Uri("http://stub") },
            NullLogger<DbGatewayClient>.Instance);
        var dispatcher = new NotificationDispatcher(
            Array.Empty<INotificationChannel>(), NullLogger<NotificationDispatcher>.Instance);
        return new MlProposerGate(db, runtime, dispatcher);
    }

    [Fact]
    public async Task LayerDisabled_BlocksBeforeAnyWork()
    {
        var gate = Build(new MlSettings { Enabled = false }, earliest: DateTime.UtcNow.AddDays(-30));
        var d = await gate.EvaluateAsync(CancellationToken.None);
        Assert.False(d.Allowed);
        Assert.Equal("layer_disabled", d.Reason);
        Assert.False(d.Journal); // a persistent off-state is shown by the banner, not journalled every cycle
    }

    [Fact]
    public async Task ProposalsDisabled_Blocks_ButLayerStaysOn()
    {
        var gate = Build(new MlSettings { Enabled = true, ProposalsEnabled = false }, earliest: DateTime.UtcNow.AddDays(-30));
        var d = await gate.EvaluateAsync(CancellationToken.None);
        Assert.False(d.Allowed);
        Assert.Equal("proposals_disabled", d.Reason);
    }

    [Fact]
    public async Task ImmatureHistory_Blocks_AndIsJournalledAsProgress()
    {
        var gate = Build(new MlSettings { Enabled = true, ProposalsEnabled = true, MinHistoryDays = 7 },
            earliest: DateTime.UtcNow.AddDays(-2));
        var d = await gate.EvaluateAsync(CancellationToken.None);
        Assert.False(d.Allowed);
        Assert.Equal("history_immature", d.Reason);
        Assert.True(d.Journal);
        Assert.Equal(7, d.RequiredDays);
        Assert.InRange(d.HistoryDays, 1.5, 2.5);
    }

    [Fact]
    public async Task MatureHistory_Allows()
    {
        var gate = Build(new MlSettings { Enabled = true, ProposalsEnabled = true, MinHistoryDays = 7 },
            earliest: DateTime.UtcNow.AddDays(-30));
        var d = await gate.EvaluateAsync(CancellationToken.None);
        Assert.True(d.Allowed);
    }

    [Fact]
    public async Task ZeroGate_AllowsRegardlessOfHistory()
    {
        var gate = Build(new MlSettings { Enabled = true, ProposalsEnabled = true, MinHistoryDays = 0 },
            earliest: DateTime.UtcNow.AddMinutes(-5));
        var d = await gate.EvaluateAsync(CancellationToken.None);
        Assert.True(d.Allowed);
    }

    [Fact]
    public async Task EmptyOrUnreachableLog_Blocks_ButNotJournalled()
    {
        var gate = Build(new MlSettings { Enabled = true, ProposalsEnabled = true, MinHistoryDays = 7 }, earliest: null);
        var d = await gate.EvaluateAsync(CancellationToken.None);
        Assert.False(d.Allowed);
        Assert.Equal("history_immature", d.Reason);
        Assert.False(d.Journal); // can't tell an empty log from an outage — don't record a misleading "0 of 7"
    }
}
