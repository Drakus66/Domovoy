// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Composes the weekly "living home" digest (roadmap Epic 3J tail 5, extends the 2N diary): 3–5 lines, tone of
/// the diary, on the week's intervention dynamics (the "trust currency"), new findings and proposed rule
/// amendments. Pure + deterministic (testable): the scheduling service gathers the metrics and dispatches the
/// text this returns. Russian, template-NLG, offline — same principles as the 2N narrative.
///
/// <para>"Silence is a feature" (2N): a week with nothing to say returns <c>null</c> and no digest is sent.</para>
/// </summary>
public static class WeeklyDigestBuilder
{
    /// <summary>The week's numbers the digest speaks to.</summary>
    public sealed record Metrics(int Firings, int Overrides, int NewFindings, int Amendments);

    /// <summary>The digest text, or null when the week had nothing worth a nudge.</summary>
    public static string? Build(Metrics m)
    {
        if (m.Firings == 0 && m.NewFindings == 0 && m.Amendments == 0) return null;

        var lines = new List<string>();

        if (m.Firings > 0)
        {
            if (m.Overrides <= 0)
            {
                lines.Add($"За неделю автоматизации отработали {Runs(m.Firings)} — и вы ни разу их не поправили. Дом попал в ритм.");
            }
            else
            {
                var rate = (double)m.Overrides / m.Firings;
                lines.Add($"Автоматизации сработали {Runs(m.Firings)}, вы вмешались {Times(m.Overrides)} ({rate:P0}).");
            }
        }

        if (m.NewFindings > 0)
            lines.Add($"Появилось новых наблюдений: {m.NewFindings} — они ждут решения в «Предложениях».");

        if (m.Amendments > 0)
            lines.Add($"Предложено правок к правилам: {m.Amendments} — уточнить или выключить то, с чем вы спорите.");

        return string.Join(" ", lines);
    }

    // Simple RU pluralization for the two counters the digest uses ("раз"/"раза" — "раз" is invariant here).
    private static string Runs(int n) => $"{n} раз";
    private static string Times(int n) => $"{n} раз";
}
