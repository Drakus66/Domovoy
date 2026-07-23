// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the ML substrate (roadmap Epic 2A; tasks Epic 2P). The model registry and the ML
/// tasks live in the DbGateway; training, backtest and the data-sufficiency check run on the AutomationService
/// (which owns the trainer + model loader). The trainer-internal task-status endpoint is deliberately NOT
/// proxied — only services write training status. Gated on <c>models.manage</c>.
/// </summary>
[ApiController]
[Route("api/ml")]
[Authorize(Policy = WellKnownPermissions.ModelsManage)]
public class MlController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public MlController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    /// <summary>List registered model metadata (newest first).</summary>
    [HttpGet("models")]
    public Task<IActionResult> Models(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Get, "api/ml/models", ct);

    /// <summary>Delete one model version (Epic 2P retention).</summary>
    [HttpDelete("models/{id}")]
    public Task<IActionResult> DeleteModel(string id, CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Delete, $"api/ml/models/{Uri.EscapeDataString(id)}", ct);

    /// <summary>Prune old model versions per (kind, target, scope) line (Epic 2P retention).</summary>
    [HttpPost("models/prune")]
    public Task<IActionResult> PruneModels(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Post, "api/ml/models/prune", ct, forwardBody: true);

    // ===== ML tasks (Epic 2P) =====

    /// <summary>List ML training tasks.</summary>
    [HttpGet("tasks")]
    public Task<IActionResult> Tasks(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Get, "api/ml/tasks", ct);

    /// <summary>One ML training task.</summary>
    [HttpGet("tasks/{id}")]
    public Task<IActionResult> Task_(string id, CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Get, $"api/ml/tasks/{Uri.EscapeDataString(id)}", ct);

    /// <summary>Create an ML training task.</summary>
    [HttpPost("tasks")]
    public Task<IActionResult> CreateTask(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Post, "api/ml/tasks", ct, forwardBody: true);

    /// <summary>Update an ML training task (its status subdocument is trainer-owned and ignored here).</summary>
    [HttpPut("tasks/{id}")]
    public Task<IActionResult> UpdateTask(string id, CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Put, $"api/ml/tasks/{Uri.EscapeDataString(id)}", ct, forwardBody: true);

    /// <summary>Delete an ML training task (registered models keep serving until their versions are deleted).</summary>
    [HttpDelete("tasks/{id}")]
    public Task<IActionResult> DeleteTask(string id, CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Delete, $"api/ml/tasks/{Uri.EscapeDataString(id)}", ct);

    // ===== Trainer (AutomationService) =====

    /// <summary>Trigger a training run now: with <c>taskId</c> — that task, without — every enabled task.</summary>
    [HttpPost("train")]
    public Task<IActionResult> Train([FromQuery] string? taskId, CancellationToken ct)
        => Forward("automation-service", HttpMethod.Post,
            string.IsNullOrEmpty(taskId) ? "api/ml/train" : $"api/ml/train?taskId={Uri.EscapeDataString(taskId)}", ct);

    /// <summary>Backtest scorecard (Epic 2B/2P): the serving model of (target, scope) vs actual history.</summary>
    [HttpGet("backtest")]
    public Task<IActionResult> Backtest(
        [FromQuery] int days, [FromQuery] string? target, [FromQuery] string? level, [FromQuery] string? key,
        CancellationToken ct)
    {
        var query = $"days={(days > 0 ? days : 7)}";
        if (!string.IsNullOrEmpty(target)) query += $"&target={Uri.EscapeDataString(target)}";
        if (!string.IsNullOrEmpty(level)) query += $"&level={Uri.EscapeDataString(level)}";
        if (!string.IsNullOrEmpty(key)) query += $"&key={Uri.EscapeDataString(key)}";
        return Forward("automation-service", HttpMethod.Get, $"api/ml/backtest?{query}", ct);
    }

    /// <summary>Data-sufficiency check for a (prospective) ML task (Epic 2P).</summary>
    [HttpGet("data-check")]
    public Task<IActionResult> DataCheck(
        [FromQuery] string target, [FromQuery] int? windowDays, [FromQuery] int? minSamples, [FromQuery] bool? zones,
        CancellationToken ct)
    {
        var query = $"target={Uri.EscapeDataString(target)}";
        if (windowDays is > 0) query += $"&windowDays={windowDays}";
        if (minSamples is > 0) query += $"&minSamples={minSamples}";
        if (zones is not null) query += $"&zones={(zones.Value ? "true" : "false")}";
        return Forward("automation-service", HttpMethod.Get, $"api/ml/data-check?{query}", ct);
    }

    /// <summary>Scan for ML-task candidates now (Epic 2P): consumable targets with enough history → 2C queue.</summary>
    [HttpPost("suggest-tasks")]
    public Task<IActionResult> SuggestTasks(CancellationToken ct)
        => Forward("automation-service", HttpMethod.Post, "api/ml/suggest-tasks", ct);

    /// <summary>ML archetype classifier (Epic 2D): train on the device population, list disagreements.</summary>
    [HttpPost("classify-archetypes")]
    public Task<IActionResult> ClassifyArchetypes(CancellationToken ct)
        => Forward("automation-service", HttpMethod.Post, "api/ml/classify-archetypes", ct);

    // ===== ML-activity journal (Epic 3I) =====

    /// <summary>The ML-activity journal (the proactive layer's "pulse"), newest-first; <c>?source=</c>/<c>?limit=</c>.</summary>
    [HttpGet("activity")]
    public Task<IActionResult> Activity(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Get, "api/ml/activity" + Request.QueryString.Value, ct);

    // forwardBody streams the incoming request body + content type upstream (POST/PUT payloads); without it
    // the upstream call is body-less (GET/DELETE and body-free POSTs).
    private async Task<IActionResult> Forward(
        string client, HttpMethod method, string path, CancellationToken ct, bool forwardBody = false)
    {
        var http = _httpClientFactory.CreateClient(client);
        using var request = new HttpRequestMessage(method, path);
        if (forwardBody)
        {
            request.Content = new StreamContent(Request.Body);
            if (!string.IsNullOrEmpty(Request.ContentType))
                request.Content.Headers.TryAddWithoutValidation("Content-Type", Request.ContentType);
        }

        using var upstream = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await upstream.Content.ReadAsStringAsync(ct);

        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = string.IsNullOrEmpty(body) ? null : body,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }
}
