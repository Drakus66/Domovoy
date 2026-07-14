// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Narrative;

/// <summary>
/// Resolves the renderer to use for a locale (roadmap Epic 2N): the highest available <see cref="RenderTier"/>
/// for that locale, always falling back to a deterministic renderer so the diary never depends on an optional
/// assisted (LLM/plugin) layer. If the requested locale has no renderer, any deterministic renderer serves.
/// </summary>
public sealed class NarrativeRendererSelector : INarrativeRendererSelector
{
    private readonly IReadOnlyList<INarrativeRenderer> _renderers;

    public NarrativeRendererSelector(IEnumerable<INarrativeRenderer> renderers)
        => _renderers = renderers.ToList();

    public INarrativeRenderer ResolveFor(string locale)
    {
        var exact = _renderers
            .Where(r => string.Equals(r.Locale, locale, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => (int)r.Tier)
            .FirstOrDefault();
        if (exact is not null) return exact;

        return _renderers.FirstOrDefault(r =>
                   r.Tier == RenderTier.Deterministic &&
                   string.Equals(r.Locale, locale, StringComparison.OrdinalIgnoreCase))
               ?? _renderers.FirstOrDefault(r => r.Tier == RenderTier.Deterministic)
               ?? _renderers.FirstOrDefault()
               ?? throw new InvalidOperationException("No narrative renderer is registered");
    }
}
