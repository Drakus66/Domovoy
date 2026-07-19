// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the backup platform (roadmap Epic 3A) — the bundles live on the DbGateway side
/// (the only Mongo-talking service). JSON endpoints mirror <see cref="SettingsController"/>; download and
/// upload stream the zip instead of buffering it as a string. Gated on <c>system.admin</c> — a bundle contains
/// the entire household's data and a restore replaces it.
/// </summary>
[ApiController]
[Route("api/backup")]
[Authorize(Policy = WellKnownPermissions.SystemAdmin)]
public class BackupController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public BackupController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet("settings")]
    public Task<IActionResult> GetSettings(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/backup/settings", ct);

    [HttpPut("settings")]
    public Task<IActionResult> PutSettings(CancellationToken ct)
        => Forward(HttpMethod.Put, "api/backup/settings", ct, forwardBody: true);

    [HttpGet("")]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/backup/", ct);

    [HttpPost("run")]
    public Task<IActionResult> Run(CancellationToken ct)
        => Forward(HttpMethod.Post, "api/backup/run", ct);

    [HttpDelete("{file}")]
    public Task<IActionResult> Delete(string file, CancellationToken ct)
        => Forward(HttpMethod.Delete, $"api/backup/{Uri.EscapeDataString(file)}", ct);

    [HttpPost("{file}/restore")]
    public Task<IActionResult> Restore(string file, CancellationToken ct)
        => Forward(HttpMethod.Post,
            $"api/backup/{Uri.EscapeDataString(file)}/restore" + Request.QueryString.Value, ct);

    /// <summary>Stream the bundle through instead of buffering — bundles can be hundreds of MB.</summary>
    [HttpGet("{file}/download")]
    public async Task<IActionResult> Download(string file, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("db-gateway");
        var upstream = await client.GetAsync(
            $"api/backup/{Uri.EscapeDataString(file)}/download",
            HttpCompletionOption.ResponseHeadersRead, ct);

        if (!upstream.IsSuccessStatusCode)
        {
            var error = await upstream.Content.ReadAsStringAsync(ct);
            upstream.Dispose();
            return new ContentResult
            {
                StatusCode = (int)upstream.StatusCode,
                Content = string.IsNullOrEmpty(error) ? null : error,
                ContentType = "application/json",
            };
        }

        HttpContext.Response.RegisterForDispose(upstream);
        var stream = await upstream.Content.ReadAsStreamAsync(ct);
        return File(stream, "application/zip", fileDownloadName: file);
    }

    /// <summary>Host migration: relay the raw zip body upstream without a size cap.</summary>
    [HttpPost("upload")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("db-gateway");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/backup/upload")
        {
            Content = new StreamContent(Request.Body),
        };
        request.Content.Headers.TryAddWithoutValidation(
            "Content-Type", Request.ContentType ?? "application/zip");

        using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await upstream.Content.ReadAsStringAsync(ct);
        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = string.IsNullOrEmpty(body) ? null : body,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }

    private async Task<IActionResult> Forward(
        HttpMethod method, string relativePath, CancellationToken ct, bool forwardBody = false)
    {
        var client = _httpClientFactory.CreateClient("db-gateway");
        using var request = new HttpRequestMessage(method, relativePath);

        if (forwardBody)
        {
            request.Content = new StreamContent(Request.Body);
            if (!string.IsNullOrEmpty(Request.ContentType))
                request.Content.Headers.TryAddWithoutValidation("Content-Type", Request.ContentType);
        }

        using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await upstream.Content.ReadAsStringAsync(ct);

        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = string.IsNullOrEmpty(body) ? null : body,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }
}
