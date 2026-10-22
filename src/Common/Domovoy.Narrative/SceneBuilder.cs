// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Narrative;

namespace Domovoy.Narrative;

/// <summary>Knobs for coalescing beats into scenes (roadmap Epic 2N, Phase 1).</summary>
public sealed class SceneBuilderOptions
{
    /// <summary>Beats sharing a causal root within this window merge into one scene.</summary>
    public TimeSpan CoalesceWindow { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How far back a rule-caused scene looks for a System-sensor transition to adopt as its cause.</summary>
    public TimeSpan CauseWindow { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Pure, deterministic coalescing of language-neutral <see cref="Beat"/>s into <see cref="Scene"/>s
/// (roadmap Epic 2N, Phase 1): beats sharing a causal root <c>(RuleId | mode-change | manual device,
/// time-bucket)</c> become one sentence, so a rule firing that drives N devices reads as one line, not N.
/// Then a rule-caused scene adopts a preceding System-sensor transition as its language-neutral cause
/// («стало темнеть → дух дома засветил веранду»), and that impersonal scene is consumed. No Mongo, no ML.
/// </summary>
public static class SceneBuilder
{
    public const string HomeModeCapability = "home_mode";

    public static List<Scene> Build(IReadOnlyList<Beat> beats, SceneBuilderOptions? options = null)
    {
        options ??= new SceneBuilderOptions();
        var ordered = beats.OrderBy(b => b.Timestamp).ToList();

        // 1) Coalesce by causal root + time bucket.
        var order = new List<Scene>();
        var byKey = new Dictionary<string, Scene>(StringComparer.Ordinal);
        var windowTicks = Math.Max(1, options.CoalesceWindow.Ticks);

        foreach (var b in ordered)
        {
            var root = RootOf(b);
            var bucket = b.Timestamp.Ticks / windowTicks;
            var key = $"{root}|{bucket}";

            if (!byKey.TryGetValue(key, out var scene))
            {
                scene = new Scene
                {
                    Id = key,
                    StartedAt = b.Timestamp,
                    EndedAt = b.Timestamp,
                    Actor = b.Actor,
                    CausalRootKey = root,
                    Mode = b.Mode,
                    Beats = new List<Beat>(),
                };
                byKey[key] = scene;
                order.Add(scene);
            }

            scene.Beats.Add(b);
            if (b.Timestamp < scene.StartedAt) scene.StartedAt = b.Timestamp;
            if (b.Timestamp > scene.EndedAt) scene.EndedAt = b.Timestamp;
            // Dominant actor: an actor beat wins over an impersonal placeholder.
            if (scene.Actor == PersonaRole.Impersonal && b.Actor != PersonaRole.Impersonal)
                scene.Actor = b.Actor;
        }

        // 2) Causal linkage: a rule-caused scene adopts the nearest preceding impersonal scene as its cause.
        // Only cause-candidate beats qualify (System sensors) — a hardware telemetry tick that merely
        // happened to precede the rule must not be narrated as its reason.
        var impersonal = order
            .Where(s => s.Actor == PersonaRole.Impersonal && s.Beats.Count > 0 && s.Beats[0].CauseCandidate)
            .ToList();
        var consumed = new HashSet<Scene>();

        foreach (var s in order)
        {
            if (s.Actor == PersonaRole.Impersonal) continue;
            if (!s.CausalRootKey.StartsWith("rule:", StringComparison.Ordinal)) continue;

            var cause = impersonal
                .Where(i => !consumed.Contains(i)
                            && i.StartedAt <= s.StartedAt
                            && s.StartedAt - i.StartedAt <= options.CauseWindow)
                .OrderByDescending(i => i.StartedAt)
                .FirstOrDefault();

            if (cause is not null)
            {
                s.CauseBeat = cause.Beats[0];
                consumed.Add(cause);
            }
        }

        return order.Where(s => !consumed.Contains(s)).OrderBy(s => s.StartedAt).ToList();
    }

    private static string RootOf(Beat b)
    {
        if (b.Actor == PersonaRole.Impersonal)
            return $"impersonal:{b.CapabilityId}";
        if (!string.IsNullOrEmpty(b.RuleId))
            return $"rule:{b.RuleId}";
        if (string.Equals(b.CapabilityId, HomeModeCapability, StringComparison.OrdinalIgnoreCase))
            return $"mode:{b.OldValue}->{b.NewValue}";
        return $"manual:{(string.IsNullOrEmpty(b.DeviceId) ? b.DeviceRef : b.DeviceId)}";
    }
}
