// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the natural-language assistant extension point (roadmap Epic 2H). Forwards to the
/// AutomationService, which hosts the connector (it owns rule authoring + 1F attribution). The capability is a
/// stub gated by a feature flag: while disabled the upstream returns a graceful "not configured" result, so the
/// UI degrades cleanly. Mirrors <see cref="ProposalsController"/> in fronting the AutomationService.
/// </summary>
[ApiController]
[Route("api/assistant")]
public class AssistantController : ProxyController
{
    public AssistantController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [HttpGet("status")]
    public Task<IActionResult> Status(CancellationToken ct)
        => Forward("api/assistant/status", ct);

    [HttpPost("author-rule")]
    public Task<IActionResult> AuthorRule(CancellationToken ct)
        => Forward("api/assistant/author-rule", ct);

    [HttpPost("explain")]
    public Task<IActionResult> Explain(CancellationToken ct)
        => Forward("api/assistant/explain", ct);
}
