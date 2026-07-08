// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Narrative;

namespace Domovoy.Narrative;

/// <summary>Capability tier of a renderer — the selector prefers the highest available for a locale.</summary>
public enum RenderTier
{
    /// <summary>Template/slot-filling from the language pack — offline, deterministic, always available.</summary>
    Deterministic,

    /// <summary>LLM/plugin-assisted styling (Epic 2H / Phase 4) — optional, degrades to Deterministic.</summary>
    Assisted,
}

/// <summary>The rendered prose for one day plus provenance of which renderer produced it.</summary>
public sealed record RenderedStory(string Paragraph, string RendererTier);

/// <summary>
/// Renders a language-neutral <see cref="DayStory"/> into a dated prose paragraph for one locale
/// (roadmap Epic 2N). The core never assembles prose itself — it builds the IR and calls the renderer
/// resolved for the configured locale. The deterministic template renderer is the mandatory default; an
/// optional LLM/plugin renderer may register on top and is used only when available, else we fall back.
/// The <see cref="NarrativeState"/> is a reference type mutated in place (deterministic cooldown cursor).
/// </summary>
public interface INarrativeRenderer
{
    /// <summary>BCP-ish language tag this renderer serves (e.g. <c>ru</c>).</summary>
    string Locale { get; }

    RenderTier Tier { get; }

    RenderedStory Render(DayStory day, LanguagePack pack, NarrativeState state);
}

/// <summary>Picks the best renderer for a locale — highest available tier, always resolving to a deterministic one.</summary>
public interface INarrativeRendererSelector
{
    INarrativeRenderer ResolveFor(string locale);
}
