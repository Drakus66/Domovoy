// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Delivery and updates (roadmap Epic 3K). A thin reverse proxy over two upstreams: the settings live
/// with the rest of the domain state on the DbGateway, while the registry, planning and execution are
/// the delivery service's job.
/// <para>
/// Gated on <c>system.admin</c> — pressing this replaces running software on the house — and it is the
/// only route to the delivery service, which publishes no ports outside <c>domovoy-network</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/updates")]
[Authorize(Policy = WellKnownPermissions.SystemAdmin)]
public class UpdatesController : ProxyController
{
    public UpdatesController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    // ---- настройки (db-gateway) ----

    [HttpGet("settings")]
    public Task<IActionResult> GetSettings(CancellationToken ct)
        => ForwardTo("db-gateway", "api/update-settings", ct);

    [HttpPut("settings")]
    public Task<IActionResult> PutSettings(CancellationToken ct)
        => ForwardTo("db-gateway", "api/update-settings", ct);

    // ---- состав и планирование (служба обновлений) ----

    /// <summary>Installed versus available, per component — the table the settings page renders.</summary>
    [HttpGet("components")]
    public Task<IActionResult> Components(CancellationToken ct)
        => ForwardTo("updater", "api/components", ct);

    [HttpGet("check")]
    public Task<IActionResult> Check(CancellationToken ct)
        => ForwardTo("updater", "api/updates/check", ct);

    /// <summary>
    /// What updating these components would actually do — including what gets pulled in and why.
    /// Deliberately a separate call from <see cref="Apply"/>: the confirmation dialog has to show the
    /// consequences before anything happens.
    /// </summary>
    [HttpPost("plan")]
    public Task<IActionResult> Plan(CancellationToken ct)
        => ForwardTo("updater", "api/updates/plan", ct);

    [HttpPost("apply")]
    public Task<IActionResult> Apply(CancellationToken ct)
        => ForwardTo("updater", "api/updates/apply", ct);

    /// <summary>
    /// Progress of the current run. Polled rather than pushed: an update recreates this very gateway
    /// and the WebUI near the end, so the connection is expected to drop — the state lives in a file
    /// on the host and the browser picks the story back up on reconnect.
    /// </summary>
    [HttpGet("status")]
    public Task<IActionResult> Status(CancellationToken ct)
        => ForwardTo("updater", "api/updates/status", ct);

    [HttpGet("history")]
    public Task<IActionResult> History(CancellationToken ct)
        => ForwardTo("updater", "api/updates/history", ct);

    [HttpPost("rollback")]
    public Task<IActionResult> Rollback(CancellationToken ct)
        => ForwardTo("updater", "api/updates/rollback", ct);
}
