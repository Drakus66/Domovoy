// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Updater.Model;

using Microsoft.Extensions.Logging;

namespace Domovoy.Updater.Services;

/// <summary>Verdict on whether a candidate release set may be applied.</summary>
public sealed record TrustVerdict(bool Trusted, string? Reason = null)
{
    public static TrustVerdict Ok() => new(true);
    public static TrustVerdict Reject(string reason) => new(false, reason);
}

/// <summary>
/// The trust seam (roadmap Epic 3K). Deliberately an interface with a weak default, because the
/// strong implementation is a planned follow-up and must not require reworking anything around it.
/// <para>
/// v1 — <see cref="ConstantSourceTrustPolicy"/>: the set came from the compiled-in registry.
/// Next — a signed release manifest verified with a public key embedded in this binary, which is
/// what actually closes registry-account compromise and tag substitution. Because images are
/// already addressed by digest everywhere, adding it is a second implementation of this interface
/// and nothing else.
/// </para>
/// </summary>
public interface IReleaseTrustPolicy
{
    TrustVerdict Verify(IReadOnlyCollection<AvailableComponent> candidates);
}

/// <summary>
/// v1 policy: accept a set only if every image in it lives under the compiled-in registry namespace.
/// <para>
/// What this catches: a mis-set channel, a stray local image, a container recreated by hand from
/// somewhere else. What it does <b>not</b> catch: a compromised registry account, or a fork that
/// edited <see cref="ReleaseSource"/> and rebuilt. That boundary needs a signature, not a constant.
/// </para>
/// </summary>
public sealed class ConstantSourceTrustPolicy : IReleaseTrustPolicy
{
    private readonly ILogger<ConstantSourceTrustPolicy> _logger;

    public ConstantSourceTrustPolicy(ILogger<ConstantSourceTrustPolicy> logger) => _logger = logger;

    public TrustVerdict Verify(IReadOnlyCollection<AvailableComponent> candidates)
    {
        foreach (var c in candidates)
        {
            var image = $"{ReleaseSource.Registry}/{c.Repository}";
            if (!ReleaseSource.IsOurs(image))
            {
                _logger.LogError(
                    "Release rejected: '{Component}' resolves to '{Image}', which is outside the built-in source",
                    c.Name, image);
                return TrustVerdict.Reject(
                    $"Компонент '{c.Name}' указывает на образ '{image}' вне встроенного источника обновлений.");
            }

            if (string.IsNullOrWhiteSpace(c.Digest))
            {
                return TrustVerdict.Reject(
                    $"Для компонента '{c.Name}' не получен digest образа — обновление по подвижному тегу запрещено.");
            }
        }

        return TrustVerdict.Ok();
    }
}
