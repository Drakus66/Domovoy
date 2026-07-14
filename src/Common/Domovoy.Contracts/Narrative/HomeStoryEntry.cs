// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Narrative;

/// <summary>
/// A materialized diary day (roadmap Epic 2N) — persisted in the <c>home_story</c> collection by the
/// background builder, one document per narrated day. It stores <b>both</b> the rendered prose
/// (<see cref="Paragraph"/>) and the language-neutral <see cref="Scenes"/> IR, so the day can be
/// re-rendered in another locale — or later restyled by an LLM (Phase 4) — <i>without re-mining history</i>.
/// Significance is computed once at build time (<see cref="DayScore"/>) and drives tier/TTL decay (Epic 1B):
/// under 7 days everything is kept, deeper history keeps only significant days.
/// </summary>
public class HomeStoryEntry
{
    /// <summary>Stable id (GUID string). Server-assigned on build.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The narrated local calendar day, stored as a UTC-midnight instant (also the time-series timeField).</summary>
    public DateTime Date { get; set; }

    /// <summary>Language the <see cref="Paragraph"/> was rendered in (e.g. <c>ru</c>).</summary>
    public string Locale { get; set; } = "ru";

    /// <summary>The rendered prose paragraph for the day.</summary>
    public string Paragraph { get; set; } = string.Empty;

    /// <summary>The language-neutral story IR — enables re-render in another locale without re-mining.</summary>
    public List<Scene> Scenes { get; set; } = new();

    /// <summary>Aggregate significance of the day (roadmap Epic 2N) — the tier/retention signal.</summary>
    public double DayScore { get; set; }

    /// <summary>Retention tier: 0 = recent (&lt; 7 days, keep-all), ≥ 1 = deep history (significant only).</summary>
    public int Tier { get; set; }

    /// <summary>Which renderer produced the text: <c>deterministic</c> (template) or <c>assisted</c> (LLM/plugin).</summary>
    public string RendererTier { get; set; } = "deterministic";

    /// <summary>Version of the language pack the text was rendered from — a bump can trigger a re-render.</summary>
    public int PackVersion { get; set; }

    public DateTime BuiltAt { get; set; } = DateTime.UtcNow;

    /// <summary>Time-series timeField for the <c>home_story</c> collection (mirrors <see cref="Date"/>).</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
