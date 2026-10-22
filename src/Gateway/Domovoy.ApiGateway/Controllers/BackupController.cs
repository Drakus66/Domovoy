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
public class BackupController : ProxyController
{
    public BackupController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [HttpGet("settings")]
    public Task<IActionResult> GetSettings(CancellationToken ct)
        => Forward("api/backup/settings", ct);

    [HttpPut("settings")]
    public Task<IActionResult> PutSettings(CancellationToken ct)
        => Forward("api/backup/settings", ct);

    [HttpGet("")]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward("api/backup/", ct);

    [HttpPost("run")]
    public Task<IActionResult> Run(CancellationToken ct)
        => Forward("api/backup/run", ct);

    [HttpDelete("{file}")]
    public Task<IActionResult> Delete(string file, CancellationToken ct)
        => Forward($"api/backup/{Uri.EscapeDataString(file)}", ct);

    [HttpPost("{file}/restore")]
    public Task<IActionResult> Restore(string file, CancellationToken ct)
        => Forward($"api/backup/{Uri.EscapeDataString(file)}/restore", ct);

    /// <summary>
    /// Скачивание бандла. Отдельного кода больше не требует: базовая прокачка и так переливает ответ
    /// потоком и переносит заголовки содержимого, включая Content-Disposition с именем файла.
    /// </summary>
    [HttpGet("{file}/download")]
    public Task<IActionResult> Download(string file, CancellationToken ct)
        => Forward($"api/backup/{Uri.EscapeDataString(file)}/download", ct);

    /// <summary>Переезд на другой хост: сырое тело zip уходит наверх потоком, без ограничения размера.</summary>
    [HttpPost("upload")]
    [DisableRequestSizeLimit]
    public Task<IActionResult> Upload(CancellationToken ct)
        => Forward("api/backup/upload", ct);
}
