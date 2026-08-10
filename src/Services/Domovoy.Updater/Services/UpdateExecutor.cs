// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net.Http.Json;
using System.Text.Json;

using Docker.DotNet;
using Docker.DotNet.Models;

using Domovoy.Updater.Configuration;
using Domovoy.Updater.Model;

using Microsoft.Extensions.Logging;

namespace Domovoy.Updater.Services;

/// <summary>
/// Carries out an update plan (roadmap Epic 3K).
///
/// <para>Order of business: preflight → backup → topology (if the plan includes it) → pull by digest
/// → recreate group by group → replace self. The updater goes last because a container cannot
/// recreate itself: the final step launches a throwaway agent from the <i>new</i> image that waits
/// for this one to die and brings it back.</para>
///
/// <para>Progress is written to <c>state/current-run.json</c> on the host, not held in memory,
/// because the api-gateway and the WebUI are recreated near the end — the browser <i>will</i> lose
/// its connection mid-flight, and must be able to pick the story back up on reconnect.</para>
/// </summary>
public sealed class UpdateExecutor
{
    /// <summary>
    /// The db-gateway route that takes a backup. A constant rather than a literal at the call site because
    /// <c>UpdaterBackupRouteTests</c> checks it against the routes the DbGateway actually declares: a typo
    /// here is a 404 swallowed as a warning, i.e. the pre-update safety backup silently not happening.
    /// </summary>
    public const string BackupRunPath = "/api/backup/run";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly UpdaterOptions _options;
    private readonly DockerInventory _inventory;
    private readonly TopologyManager _topology;
    private readonly ComposeRunner _compose;
    private readonly IReleaseTrustPolicy _trust;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<UpdateExecutor> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public UpdateExecutor(
        UpdaterOptions options,
        DockerInventory inventory,
        TopologyManager topology,
        ComposeRunner compose,
        IReleaseTrustPolicy trust,
        IHttpClientFactory httpFactory,
        ILogger<UpdateExecutor> logger)
    {
        _options = options;
        _inventory = inventory;
        _topology = topology;
        _compose = compose;
        _trust = trust;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>Last known run state — what the UI polls while it reconnects.</summary>
    public UpdateRunState? GetCurrentRun()
    {
        try
        {
            if (!File.Exists(_options.CurrentRunFile)) return null;
            return JsonSerializer.Deserialize<UpdateRunState>(File.ReadAllText(_options.CurrentRunFile));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать состояние прогона");
            return null;
        }
    }

    public IReadOnlyList<UpdateRunState> GetHistory()
    {
        try
        {
            if (!File.Exists(_options.HistoryFile)) return Array.Empty<UpdateRunState>();
            return JsonSerializer.Deserialize<List<UpdateRunState>>(File.ReadAllText(_options.HistoryFile))
                   ?? new List<UpdateRunState>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать историю обновлений");
            return Array.Empty<UpdateRunState>();
        }
    }

    /// <summary>
    /// Applies a plan. Refuses to start a second run concurrently — two updates at once on one house
    /// is not a scenario worth supporting.
    /// </summary>
    public async Task<UpdateRunState> ApplyAsync(
        UpdatePlan plan,
        UpdateCatalogSnapshot catalog,
        bool backupFirst,
        CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct))
            throw new InvalidOperationException("Обновление уже выполняется.");

        try
        {
            return await RunAsync(plan, catalog, backupFirst, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UpdateRunState> RunAsync(
        UpdatePlan plan, UpdateCatalogSnapshot catalog, bool backupFirst, CancellationToken ct)
    {
        var run = new UpdateRunState
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            StartedAt = DateTimeOffset.UtcNow,
            Channel = catalog.Channel,
            Status = "running",
            Plan = plan.All.ToList(),
            Previous = plan.All.ToDictionary(p => p.Component, p => p.FromVersion),
        };
        Persist(run);

        try
        {
            // --- preflight -------------------------------------------------------------
            run = Step(run, "preflight", "running");

            var candidates = plan.All
                .Select(p => new AvailableComponent(
                    p.Component, p.Repository, "", p.Digest, p.ToVersion,
                    new ComponentDeps { Component = p.Component, Container = p.Container, Version = p.ToVersion }))
                .ToList();

            var verdict = _trust.Verify(candidates);
            if (!verdict.Trusted)
                throw new InvalidOperationException($"Источник обновления отклонён: {verdict.Reason}");

            if (!await _inventory.IsAvailableAsync(ct))
                throw new InvalidOperationException("Docker недоступен — обновление невозможно.");

            EnsureDiskSpace();
            run = Step(run, "preflight", "ok");

            // --- бэкап -----------------------------------------------------------------
            if (backupFirst)
            {
                run = Step(run, "backup", "running");
                var file = await RequestBackupAsync(ct);
                run = run with { BackupFile = file };
                run = Step(run, "backup", file is null ? "skipped" : "ok", file);
            }
            else
            {
                run = Step(run, "backup", "skipped", "отключено настройкой");
            }

            // --- топология -------------------------------------------------------------
            var topologyMove = plan.All.FirstOrDefault(p => p.Component == ReleaseSource.TopologyComponent);
            if (topologyMove is not null)
            {
                run = Step(run, "topology", "running", $"{topologyMove.FromVersion} → {topologyMove.ToVersion}");

                // Прежнюю версию читаем ДО применения: после него `current` уже указывает на новую,
                // и откат вернул бы систему ровно туда, откуда её пытаются откатить.
                var previousTopology = _topology.GetApplied()?.Version;

                var version = ParseMajor(topologyMove.ToVersion);
                var staged = await _topology.StageAsync(topologyMove.Digest, version, ct);
                var addedKeys = await _topology.ApplyAsync(
                    staged, version, topologyMove.Digest, topologyMove.ToVersion, ct);

                run = run with
                {
                    TopologyFrom = previousTopology,
                    TopologyTo = version,
                    EnvKeysAdded = addedKeys.ToList(),
                };
                run = Step(run, "topology", "ok",
                    addedKeys.Count > 0 ? $"в .env добавлены ключи: {string.Join(", ", addedKeys)}" : null);
            }

            // --- pull ------------------------------------------------------------------
            var images = plan.All.Where(p => p.Component != ReleaseSource.TopologyComponent).ToList();
            foreach (var move in images)
            {
                run = Step(run, $"pull:{move.Component}", "running", move.ToVersion);
                await _inventory.PullAsync(move.Repository, move.Digest, ct);
                run = Step(run, $"pull:{move.Component}", "ok");
            }

            // --- пересоздание по группам ------------------------------------------------
            var pins = new Dictionary<string, string>(_compose.ReadPins());
            var selfMove = images.FirstOrDefault(p => p.Component == ReleaseSource.SelfComponent);

            foreach (var group in plan.Groups)
            {
                var members = group.Members
                    .Where(m => m.Component != ReleaseSource.TopologyComponent)
                    .Where(m => m.Component != ReleaseSource.SelfComponent)
                    .ToList();

                if (members.Count == 0) continue;

                var label = string.Join(", ", members.Select(m => m.Component));
                run = Step(run, $"recreate:{label}", "running",
                    group.Atomic ? "атомарная группа — промежуточное состояние несовместимо" : null);

                foreach (var m in members)
                    pins[m.Container] = $"{ReleaseSource.Registry}/{m.Repository}@{m.Digest}";

                await _compose.WritePinsAsync(pins, ct);

                try
                {
                    // Атомарная группа поднимается одной командой: compose пересоздаёт её разом.
                    await _compose.UpAsync(members.Select(m => m.Container).ToList(), ct);
                }
                catch (ComposeException ex)
                {
                    _logger.LogError(ex, "Пересоздание группы '{Group}' не удалось — откатываем пины", label);
                    _compose.RestorePreviousPins();
                    await SafeUpAsync(members.Select(m => m.Container).ToList(), ct);
                    throw;
                }

                run = Step(run, $"recreate:{label}", "ok");
            }

            // Топология могла добавить или убрать сервис — сводим стек целиком.
            if (topologyMove is not null)
            {
                run = Step(run, "converge", "running");
                await _compose.UpAllAsync(ct);
                run = Step(run, "converge", "ok");
            }

            // --- самообновление ---------------------------------------------------------
            if (selfMove is not null)
            {
                pins[selfMove.Container] = $"{ReleaseSource.Registry}/{selfMove.Repository}@{selfMove.Digest}";
                await _compose.WritePinsAsync(pins, ct);

                run = Step(run, "self-update", "running", "запуск одноразового агента");
                run = run with { Status = "ok", FinishedAt = DateTimeOffset.UtcNow };
                Persist(run);
                Archive(run);

                await LaunchSelfReplaceAgentAsync(selfMove, ct);
                return run;
            }

            run = run with { Status = "ok", FinishedAt = DateTimeOffset.UtcNow };
            Persist(run);
            Archive(run);
            return run;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Обновление прервано");
            run = run with
            {
                Status = "error",
                Error = ex is ComposeException compose ? compose.Output : ex.Message,
                FinishedAt = DateTimeOffset.UtcNow,
            };
            Persist(run);
            Archive(run);
            return run;
        }
    }

    /// <summary>
    /// Rolls back to the versions recorded before the last successful run: restore the pin overlay,
    /// point <c>current</c> at the previous topology, converge. The images are still on disk, so this
    /// downloads nothing.
    /// </summary>
    public async Task<UpdateRunState> RollbackAsync(CancellationToken ct)
    {
        var last = GetHistory().LastOrDefault(r => r.Status == "ok")
            ?? throw new InvalidOperationException("Нет успешного обновления, к которому можно откатиться.");

        var run = new UpdateRunState
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            StartedAt = DateTimeOffset.UtcNow,
            Channel = last.Channel,
            Status = "running",
        };
        Persist(run);

        try
        {
            run = Step(run, "rollback:pins", "running");
            _compose.RestorePreviousPins();
            run = Step(run, "rollback:pins", "ok");

            if (last.TopologyFrom is { } previousTopology)
            {
                run = Step(run, "rollback:topology", "running", $"→ v{previousTopology}");
                var directory = Path.Combine(_options.ReleasesDirectory, $"topology-{previousTopology}");
                if (Directory.Exists(directory))
                {
                    _topology.SwitchCurrent(directory);
                    run = Step(run, "rollback:topology", "ok");
                }
                else
                {
                    run = Step(run, "rollback:topology", "skipped", "каталог предыдущего релиза не найден");
                }
            }

            run = Step(run, "rollback:converge", "running");
            await _compose.UpAllAsync(ct);
            run = Step(run, "rollback:converge", "ok");

            run = run with { Status = "rolled-back", FinishedAt = DateTimeOffset.UtcNow };
            Persist(run);
            Archive(run);
            return run;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Откат не удался");
            run = run with { Status = "error", Error = ex.Message, FinishedAt = DateTimeOffset.UtcNow };
            Persist(run);
            Archive(run);
            return run;
        }
    }

    // ---------------------------------------------------------------- шаги

    private async Task<string?> RequestBackupAsync(CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient(nameof(UpdateExecutor));
            var response = await http.PostAsync(
                $"{_options.DbGatewayBaseUrl.TrimEnd('/')}{BackupRunPath}?reason=pre-update", null, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Бэкап перед обновлением не создан: {Status}", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<BackupRunDto>(cancellationToken: ct);
            return payload?.File;
        }
        catch (Exception ex)
        {
            // Бэкап — страховка, а не условие. Если db-gateway недоступен, честнее сказать об этом
            // в журнале прогона, чем отменить обновление, которое владелец сознательно запустил.
            _logger.LogWarning(ex, "Бэкап перед обновлением не создан");
            return null;
        }
    }

    private void EnsureDiskSpace()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(_options.InstallDirectory) ?? "/");
            const long required = 2L * 1024 * 1024 * 1024; // образы Domovoy в сумме заметно меньше 2 ГБ
            if (drive.AvailableFreeSpace < required)
                throw new InvalidOperationException(
                    $"На диске меньше 2 ГБ свободного места ({drive.AvailableFreeSpace / 1024 / 1024} МБ) — " +
                    "обновление может не завершиться.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogDebug(ex, "Не удалось оценить свободное место — проверка пропущена");
        }
    }

    private async Task SafeUpAsync(IReadOnlyCollection<string> services, CancellationToken ct)
    {
        try
        {
            await _compose.UpAsync(services, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось вернуть сервисы на прежние версии после сбоя");
        }
    }

    /// <summary>
    /// Launches a one-shot container from the NEW updater image that replaces this one.
    /// A container genuinely cannot recreate itself — the moment it stops, whatever it was running
    /// stops too — so the last step has to be delegated to a process that outlives it.
    /// </summary>
    private async Task LaunchSelfReplaceAgentAsync(PlannedUpdate move, CancellationToken ct)
    {
        using var docker = new DockerClientConfiguration(new Uri(_options.DockerSocket)).CreateClient();

        var self = await docker.Containers.InspectContainerAsync(move.Container, ct);

        var agent = await docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = $"{ReleaseSource.Registry}/{move.Repository}@{move.Digest}",
            Name = $"domovoy-updater-self-replace-{DateTime.UtcNow:HHmmss}",
            Env = new List<string>
            {
                $"UPDATES__INSTALLDIRECTORY={_options.InstallDirectory}",
                $"UPDATES__DBGATEWAYBASEURL={_options.DbGatewayBaseUrl}",
            },
            Cmd = new List<string> { "--self-replace", move.Container },
            HostConfig = new HostConfig
            {
                // Тот же сокет и тот же каталог установки, что у основного контейнера: агенту нужно
                // ровно то же окружение, чтобы выполнить одну команду compose.
                Binds = self.HostConfig.Binds,
                AutoRemove = true,
                NetworkMode = self.HostConfig.NetworkMode,
            },
        }, ct);

        await docker.Containers.StartContainerAsync(agent.ID, new ContainerStartParameters(), ct);
        _logger.LogWarning(
            "Запущен агент самообновления — этот контейнер сейчас будет заменён на {Version}", move.ToVersion);
    }

    private static int ParseMajor(string version) =>
        int.TryParse(version.Split('.', '-')[0], out var v) ? v : 1;

    private UpdateRunState Step(UpdateRunState run, string name, string status, string? detail = null)
    {
        var steps = run.Steps.Where(s => s.Name != name).ToList();
        steps.Add(new UpdateStep(name, status, DateTimeOffset.UtcNow, detail));

        var updated = run with { Steps = steps.OrderBy(s => s.At).ToList() };
        Persist(updated);
        return updated;
    }

    private void Persist(UpdateRunState run)
    {
        try
        {
            Directory.CreateDirectory(_options.StateDirectory);
            File.WriteAllText(_options.CurrentRunFile, JsonSerializer.Serialize(run, Json));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить состояние прогона");
        }
    }

    private void Archive(UpdateRunState run)
    {
        try
        {
            var history = GetHistory().ToList();
            history.Add(run);
            // Держим последние 20: истории обновлений домашней системы дальше этого никто не смотрит.
            if (history.Count > 20) history = history.TakeLast(20).ToList();
            File.WriteAllText(_options.HistoryFile, JsonSerializer.Serialize(history, Json));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось записать историю обновлений");
        }
    }

    private sealed record BackupRunDto(string? File);
}
