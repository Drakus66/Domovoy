// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services.Assistant;

using Microsoft.Extensions.Options;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline unit tests for the assistant extension point's shipped stub (roadmap Epic 2H). Verifies the feature
/// flag gating and that every request degrades gracefully (Available=false with a message) while no backend is
/// configured — no infrastructure, no model.
/// </summary>
public sealed class AssistantConnectorTests
{
    private static DisabledAssistantConnector Connector(bool enabled, string provider = "") =>
        new(Options.Create(new AssistantOptions { Enabled = enabled, Provider = provider }));

    [Fact]
    public void IsUnavailable_ByDefault()
    {
        var c = Connector(enabled: false);
        Assert.False(c.IsAvailable);
        Assert.Equal(string.Empty, c.Provider);
    }

    [Fact]
    public void IsUnavailable_WhenEnabledButNoProviderNamed()
    {
        // The flag alone isn't enough — no built-in backend names a provider, so it stays inert.
        Assert.False(Connector(enabled: true).IsAvailable);
    }

    [Fact]
    public void ReportsAvailable_OnlyWhenEnabledAndProviderNamed()
    {
        var c = Connector(enabled: true, provider: "local");
        Assert.True(c.IsAvailable);
        Assert.Equal("local", c.Provider);
    }

    [Fact]
    public async Task AuthorRule_ReturnsGracefulStub()
    {
        var result = await Connector(enabled: false).AuthorRuleAsync(new AssistantAuthorRequest("turn on the light at sunset"), default);
        Assert.False(result.Available);
        Assert.Null(result.RuleId);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task Explain_ReturnsGracefulStub()
    {
        var result = await Connector(enabled: false).ExplainAsync(new AssistantExplainRequest(null, "rule-1", null), default);
        Assert.False(result.Available);
        Assert.Null(result.Explanation);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void AdvertisesBothCapabilities()
    {
        Assert.Contains("author_rule", DisabledAssistantConnector.Capabilities);
        Assert.Contains("explain", DisabledAssistantConnector.Capabilities);
    }
}
