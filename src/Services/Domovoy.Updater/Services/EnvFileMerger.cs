// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text;

namespace Domovoy.Updater.Services;

/// <summary>
/// Keeps the host <c>.env</c> current without ever overwriting the owner's choices (roadmap Epic 3K).
///
/// <para><b>The rule.</b> The template that ships with a release is the reference; the file on the
/// host belongs to the owner. Merging only ever <i>adds</i> keys the host does not have yet, with the
/// template's default and the comment written above them. Nothing is edited, reordered or removed —
/// a password, a device path or a domain the owner set is untouchable.</para>
///
/// <para>This is the same split as a distribution's package files versus <c>/etc</c>, and it is what
/// makes "a release introduced a new setting" a non-event instead of a manual chore.</para>
/// </summary>
public static class EnvFileMerger
{
    /// <summary>Result of a merge: which keys were added, and the file content to write.</summary>
    public sealed record MergeResult(string Content, IReadOnlyList<string> AddedKeys)
    {
        public bool Changed => AddedKeys.Count > 0;
    }

    /// <summary>
    /// Merges <paramref name="template"/> into <paramref name="existing"/>.
    /// Both are raw <c>.env</c> text; the host file may be empty (fresh install).
    /// </summary>
    public static MergeResult Merge(string existing, string template)
    {
        var present = ParseKeys(existing);
        var added = new List<string>();
        var appended = new StringBuilder();

        // Комментарии, накопленные над очередным ключом шаблона: если ключ переезжает на хост,
        // пояснение едет вместе с ним — иначе владелец увидит голое имя без смысла.
        var pendingComments = new List<string>();

        foreach (var rawLine in SplitLines(template))
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                pendingComments.Clear();
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                pendingComments.Add(line);
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
            {
                pendingComments.Clear();
                continue;
            }

            var key = trimmed[..eq].Trim();
            if (present.Contains(key))
            {
                pendingComments.Clear();
                continue;
            }

            if (added.Count == 0)
            {
                appended.AppendLine();
                appended.AppendLine("# ---- Добавлено при обновлении: новые настройки из шаблона релиза ----");
            }

            appended.AppendLine();
            foreach (var comment in pendingComments) appended.AppendLine(comment);
            appended.AppendLine(trimmed);

            added.Add(key);
            pendingComments.Clear();
        }

        if (added.Count == 0) return new MergeResult(existing, Array.Empty<string>());

        var content = existing.Length == 0 || existing.EndsWith('\n')
            ? existing + appended.ToString().TrimStart('\r', '\n')
            : existing + Environment.NewLine + appended.ToString().TrimStart('\r', '\n');

        return new MergeResult(content, added);
    }

    /// <summary>
    /// Sets one key in place, preserving everything else. Used when the channel is switched from the
    /// UI so a later manual <c>docker compose up -d</c> cannot silently roll the channel back.
    /// </summary>
    public static string SetKey(string content, string key, string value)
    {
        var lines = SplitLines(content).ToList();
        var replaced = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith('#')) continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0 || trimmed[..eq].Trim() != key) continue;

            lines[i] = $"{key}={value}";
            replaced = true;
            break;
        }

        if (!replaced) lines.Add($"{key}={value}");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Reads one key, or null when it is absent or commented out.</summary>
    public static string? GetKey(string content, string key)
    {
        foreach (var line in SplitLines(content))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#')) continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0 || trimmed[..eq].Trim() != key) continue;

            return trimmed[(eq + 1)..].Trim();
        }

        return null;
    }

    private static HashSet<string> ParseKeys(string content)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in SplitLines(content))
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var eq = trimmed.IndexOf('=');
            if (eq > 0) keys.Add(trimmed[..eq].Trim());
        }
        return keys;
    }

    private static IEnumerable<string> SplitLines(string content) =>
        content.Split('\n').Select(l => l.TrimEnd('\r'));
}
