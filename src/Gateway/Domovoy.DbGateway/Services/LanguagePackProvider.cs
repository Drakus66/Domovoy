// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;

using Domovoy.Contracts.Narrative;
using Domovoy.Narrative;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Supplies the diary's language pack for a locale (roadmap Epic 2N): the built-in default pack (embedded
/// JSON) with the user's <c>narrative_entities</c> overrides merged on top. Cached per locale; the cache is
/// invalidated when an override is edited. Falls back to the Russian default for an unknown locale so the
/// diary always renders.
/// </summary>
public sealed class LanguagePackProvider
{
    public const string OverridesCollection = "narrative_entities";

    private readonly IMongoDatabase _db;
    private readonly ILogger<LanguagePackProvider> _logger;
    private readonly ConcurrentDictionary<string, LanguagePack> _cache = new(StringComparer.OrdinalIgnoreCase);

    public LanguagePackProvider(IMongoDatabase db, ILogger<LanguagePackProvider> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Get the effective pack (default + DB overrides) for a locale, building and caching it on first use.</summary>
    public async Task<LanguagePack> GetAsync(string? locale, CancellationToken ct = default)
    {
        var loc = string.IsNullOrWhiteSpace(locale) ? "ru" : locale.Trim().ToLowerInvariant();
        if (_cache.TryGetValue(loc, out var cached)) return cached;

        var pack = LoadDefault(loc);
        try
        {
            await MergeOverridesAsync(pack, loc, ct);
        }
        catch (Exception ex)
        {
            // Overrides are best-effort; the default pack keeps the diary working.
            _logger.LogWarning(ex, "Could not merge narrative overrides for locale {Locale}", loc);
        }

        _cache[loc] = pack;
        return pack;
    }

    /// <summary>Drop the cached pack for a locale (or all) after an override edit.</summary>
    public void Invalidate(string? locale = null)
    {
        if (string.IsNullOrWhiteSpace(locale)) _cache.Clear();
        else _cache.TryRemove(locale.Trim().ToLowerInvariant(), out _);
    }

    private static LanguagePack LoadDefault(string locale)
    {
        try { return LanguagePack.LoadDefault(locale); }
        catch (FileNotFoundException) { return LanguagePack.LoadDefault("ru"); }
    }

    /// <summary>Merge <c>narrative_entities</c> pools over the default pack (personalization — Epic 2N Phase 3).</summary>
    private async Task MergeOverridesAsync(LanguagePack pack, string locale, CancellationToken ct)
    {
        var col = _db.GetCollection<NarrativeEntity>(OverridesCollection);
        var overrides = await col.Find(e => e.Locale == locale).ToListAsync(ct);
        foreach (var e in overrides)
        {
            if (e.Synonyms.Count == 0) continue;
            switch (e.Kind)
            {
                case NarrativeEntityKinds.Persona:
                    pack.Personas[e.Key] = new PersonaPool
                    {
                        Pool = e.Synonyms,
                        Cooldown = pack.Personas.TryGetValue(e.Key, out var p) ? p.Cooldown : 1,
                    };
                    break;
                case NarrativeEntityKinds.Place:
                    pack.Places.ByZoneId[e.Key] = e.Synonyms[0];
                    break;
                case NarrativeEntityKinds.Device:
                    pack.Devices.ByDeviceId[e.Key] = e.Synonyms[0];
                    break;
            }
        }
    }
}
