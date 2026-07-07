// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services.Assistant;

/// <summary>
/// The shipped assistant connector (roadmap Epic 2H): a no-op stub. There is deliberately no natural-language
/// backend yet (development runs without a model), so every request returns a graceful "not configured" result
/// rather than failing — the endpoints and UI degrade cleanly and a real implementation of
/// <see cref="IAssistantConnector"/> can replace this one later behind the <see cref="AssistantOptions"/> flag.
/// <see cref="IsAvailable"/> stays false until both the flag is on and a provider is named (which no built-in
/// backend does yet), keeping the capability visibly inert.
/// </summary>
public sealed class DisabledAssistantConnector : IAssistantConnector
{
    private const string NotConfigured =
        "The assistant is not configured. It is an extension point (Epic 2H); a natural-language backend can be enabled later.";

    private readonly AssistantOptions _options;

    public DisabledAssistantConnector(IOptions<AssistantOptions> options) => _options = options.Value;

    // Enabled alone isn't enough — a real backend must also be named. No built-in one is, so this stays false.
    public bool IsAvailable => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Provider);

    public string Provider => IsAvailable ? _options.Provider : string.Empty;

    public Task<AssistantAuthorResult> AuthorRuleAsync(AssistantAuthorRequest request, CancellationToken ct) =>
        Task.FromResult(new AssistantAuthorResult(false, null, NotConfigured));

    public Task<AssistantExplainResult> ExplainAsync(AssistantExplainRequest request, CancellationToken ct) =>
        Task.FromResult(new AssistantExplainResult(false, null, NotConfigured));

    /// <summary>The capabilities this extension point is shaped for (advertised even while unavailable).</summary>
    public static readonly IReadOnlyList<string> Capabilities = new[] { "author_rule", "explain" };
}
