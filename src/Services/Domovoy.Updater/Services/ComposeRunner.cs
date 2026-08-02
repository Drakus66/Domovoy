// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Diagnostics;
using System.Text;

using Domovoy.Updater.Configuration;
using Domovoy.Updater.Model;

using Microsoft.Extensions.Logging;

namespace Domovoy.Updater.Services;

/// <summary>
/// Applies changes through <c>docker compose</c> itself (roadmap Epic 3K).
///
/// <para><b>Why compose and not the Docker API.</b> Recreating a container by hand means replaying its
/// Config, HostConfig and NetworkingConfig correctly — and any field missed silently degrades the
/// house. Compose already does that, and it is what the owner would run manually, so the shipped
/// topology stays the single description of the system.</para>
///
/// <para><b>The pin overlay is what makes point updates possible.</b> The shipped compose refers to a
/// moving channel tag, so a bare <c>up -d</c> would drag every service to the newest build. The
/// updater therefore keeps <c>state/pinned.yml</c>, pinning every service to the exact digest that is
/// installed. Updating one component means changing one digest there and bringing up that one
/// service; everything else stays put because its pin did not move.</para>
///
/// <para><b>--project-directory is mandatory.</b> Compose resolves relative paths against the project
/// directory, and the compose file lives under <c>releases/topology-N/</c>. Without it, <c>./data</c>
/// and <c>./plugins</c> would land inside the release directory and the house would come up with
/// empty state.</para>
/// </summary>
public sealed class ComposeRunner
{
    private readonly UpdaterOptions _options;
    private readonly ILogger<ComposeRunner> _logger;

    public ComposeRunner(UpdaterOptions options, ILogger<ComposeRunner> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>Writes the pin overlay from the currently intended digests.</summary>
    public async Task WritePinsAsync(IReadOnlyDictionary<string, string> serviceToImageDigest, CancellationToken ct)
    {
        Directory.CreateDirectory(_options.StateDirectory);

        if (File.Exists(_options.PinnedFile))
            File.Copy(_options.PinnedFile, _options.PreviousPinnedFile, overwrite: true);

        var sb = new StringBuilder();
        sb.AppendLine("# СГЕНЕРИРОВАННЫЙ ФАЙЛ — НЕ РЕДАКТИРОВАТЬ.");
        sb.AppendLine("# Пины версий, которыми сейчас управляет служба обновлений (Эпик 3K).");
        sb.AppendLine("# Без него `docker compose up -d` утащил бы все сервисы на свежие сборки и");
        sb.AppendLine("# точечное обновление стало бы невозможным.");
        sb.AppendLine("services:");

        foreach (var (service, reference) in serviceToImageDigest.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"  {service}:");
            sb.AppendLine($"    image: {reference}");
        }

        await File.WriteAllTextAsync(_options.PinnedFile, sb.ToString(), ct);
    }

    /// <summary>Restores the previous pin overlay — the first move of any rollback.</summary>
    public void RestorePreviousPins()
    {
        if (File.Exists(_options.PreviousPinnedFile))
            File.Copy(_options.PreviousPinnedFile, _options.PinnedFile, overwrite: true);
    }

    /// <summary>Reads the current pins, so a partial update only rewrites what it changes.</summary>
    public IReadOnlyDictionary<string, string> ReadPins()
    {
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(_options.PinnedFile)) return pins;

        string? service = null;
        foreach (var raw in File.ReadAllLines(_options.PinnedFile))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("  ") && line.EndsWith(':') && !line.StartsWith("    "))
            {
                service = line.Trim().TrimEnd(':');
                continue;
            }

            var trimmed = line.Trim();
            if (service is not null && trimmed.StartsWith("image:", StringComparison.Ordinal))
            {
                pins[service] = trimmed["image:".Length..].Trim();
                service = null;
            }
        }

        return pins;
    }

    /// <summary>Brings up specific services without touching their dependencies.</summary>
    public Task<string> UpAsync(IReadOnlyCollection<string> services, CancellationToken ct)
    {
        var args = new List<string> { "compose" };
        args.AddRange(FileArguments());
        args.Add("up");
        args.Add("-d");
        args.Add("--no-deps");
        args.AddRange(services);
        return RunAsync(args, ct);
    }

    /// <summary>Full convergence — used after a topology change, which may add or remove services.</summary>
    public Task<string> UpAllAsync(CancellationToken ct)
    {
        var args = new List<string> { "compose" };
        args.AddRange(FileArguments());
        args.AddRange(new[] { "up", "-d", "--remove-orphans" });
        return RunAsync(args, ct);
    }

    /// <summary>Validates the effective configuration before anything is applied.</summary>
    public Task<string> ConfigAsync(CancellationToken ct)
    {
        var args = new List<string> { "compose" };
        args.AddRange(FileArguments());
        args.AddRange(new[] { "config", "-q" });
        return RunAsync(args, ct);
    }

    private IEnumerable<string> FileArguments()
    {
        yield return "--project-directory";
        yield return _options.InstallDirectory;

        yield return "-f";
        yield return Path.Combine(_options.CurrentReleaseLink, "docker-compose.yml");

        // Оверлей владельца — необязателен: на боевом хосте его может не быть вовсе.
        if (File.Exists(_options.OverrideFile))
        {
            yield return "-f";
            yield return _options.OverrideFile;
        }

        if (File.Exists(_options.PinnedFile))
        {
            yield return "-f";
            yield return _options.PinnedFile;
        }

        if (File.Exists(_options.EnvFile))
        {
            yield return "--env-file";
            yield return _options.EnvFile;
        }
    }

    private async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _options.InstallDirectory,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        _logger.LogInformation("docker {Arguments}", string.Join(' ', arguments));

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить docker CLI.");

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var output = (await stdout + Environment.NewLine + await stderr).Trim();

        if (process.ExitCode != 0)
            throw new ComposeException(
                $"docker {string.Join(' ', arguments)} завершился с кодом {process.ExitCode}.", output);

        return output;
    }
}

/// <summary>
/// A compose invocation failed. Carries the raw output because the actionable detail is almost
/// always in there — typically the owner's <c>docker-compose.override.yml</c> conflicting with a
/// service the new topology changed.
/// </summary>
public sealed class ComposeException : Exception
{
    public string Output { get; }

    public ComposeException(string message, string output) : base(message + Environment.NewLine + output) =>
        Output = output;
}
