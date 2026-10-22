// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Base for the gateway's reverse-proxy controllers — one implementation of "relay this request
/// upstream", instead of a private <c>Forward</c> in every controller.
///
/// <para><b>Почему это понадобилось.</b> Двадцать пять приватных копий (каждая по 20–30 строк) делали
/// одно и то же тремя несовместимыми способами, и расхождение было не косметическим: <b>десять</b>
/// контроллеров молча теряли query-string. Новый <c>?filter=</c> на стороне DbGateway просто не
/// доезжал — без ошибки, без записи в журнале, просто «фильтр не работает». Теперь политика одна и
/// живёт в одном месте.</para>
///
/// <para><b>Тело и ответ идут потоком.</b> Копии читали запрос и ответ целиком в строку: скачивание
/// бэкапа в сотни мегабайт и пакетная выдача телеметрии проходили через память шлюза дважды. Здесь
/// тело запроса передаётся как есть, а ответ переливается в выходной поток по мере поступления.</para>
/// </summary>
public abstract class ProxyController : ControllerBase
{
    /// <summary>Для нестандартных случаев (multipart-разбор, свой поток) — обычная прокачка их не покрывает.</summary>
    protected IHttpClientFactory HttpClientFactory { get; }

    protected ProxyController(IHttpClientFactory httpClientFactory) => HttpClientFactory = httpClientFactory;

    /// <summary>Именованный HttpClient по умолчанию для этого контроллера.</summary>
    protected virtual string UpstreamClient => "db-gateway";

    /// <summary>
    /// Передаёт текущий запрос вверх по адресу <paramref name="upstreamPath"/>.
    /// <para>Query-string добавляется из входящего запроса автоматически — кроме случая, когда путь
    /// уже содержит собственный <c>?</c> (тогда его собрал вызывающий и знает, что делает).</para>
    /// <para>Метод по умолчанию берётся из входящего запроса: у каждого действия он совпадает с
    /// глаголом его маршрута, а явное указание было ещё одним местом, где можно разойтись.</para>
    /// </summary>
    protected Task<IActionResult> Forward(
        string upstreamPath,
        CancellationToken ct,
        HttpMethod? method = null)
        => ForwardAsync(upstreamPath, ct, null, method);

    /// <summary>
    /// То же, но к явно названному сервису — для контроллеров, которые разводят действия между
    /// db-gateway, automation-service, updater и plugin-supervisor.
    /// </summary>
    protected Task<IActionResult> ForwardTo(
        string client,
        string upstreamPath,
        CancellationToken ct,
        HttpMethod? method = null)
        => ForwardAsync(upstreamPath, ct, client, method);

    private async Task<IActionResult> ForwardAsync(
        string upstreamPath, CancellationToken ct, string? client, HttpMethod? method)
    {
        var verb = method ?? new HttpMethod(Request.Method);
        var path = upstreamPath.Contains('?', StringComparison.Ordinal)
            ? upstreamPath
            : upstreamPath + Request.QueryString.Value;

        var http = HttpClientFactory.CreateClient(client ?? UpstreamClient);
        using var request = new HttpRequestMessage(verb, path);

        // GET/DELETE/HEAD тела не несут; для остальных отдаём входящий поток как есть — без чтения
        // в строку и без перекодирования, поэтому и загрузка бандла бэкапа проходит насквозь.
        if (verb != HttpMethod.Get && verb != HttpMethod.Delete && verb != HttpMethod.Head)
        {
            request.Content = new StreamContent(Request.Body);
            request.Content.Headers.TryAddWithoutValidation(
                "Content-Type", Request.ContentType ?? "application/json");
        }

        var upstream = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        HttpContext.Response.RegisterForDispose(upstream);
        return new ProxyResult(upstream);
    }
}

/// <summary>
/// Переливает ответ вышестоящего сервиса в выходной поток, не собирая его в памяти. Заголовки
/// содержимого копируются — от этого зависит, например, скачивание бэкапа
/// (<c>Content-Disposition</c> и <c>Content-Type: application/zip</c>).
/// </summary>
internal sealed class ProxyResult : IActionResult
{
    private readonly HttpResponseMessage _upstream;

    public ProxyResult(HttpResponseMessage upstream) => _upstream = upstream;

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = (int)_upstream.StatusCode;

        foreach (var header in _upstream.Content.Headers)
        {
            // Transfer-Encoding принадлежит соединению, а не сообщению: как отдавать тело клиенту,
            // решает Kestrel, и скопированный заголовок здесь только всё сломает.
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            response.Headers[header.Key] = header.Value.ToArray();
        }

        var ct = context.HttpContext.RequestAborted;
        await using var stream = await _upstream.Content.ReadAsStreamAsync(ct);
        await stream.CopyToAsync(response.Body, ct);
    }
}
