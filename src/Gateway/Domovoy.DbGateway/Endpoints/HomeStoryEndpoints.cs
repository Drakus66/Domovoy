// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Narrative;
using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Services;
using Domovoy.Narrative;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// House Diary read + preview API (roadmap Epic 2N). <c>GET /api/home-story</c> returns the materialized
/// diary (built by <c>HouseDiaryBuilder</c>); <c>POST /api/home-story/preview</c> renders a single event or a
/// handful of beats on the fly through the same deterministic NLG engine — the Phase-0 way to see the phrase
/// engine (slot → agreed form → anaphora) without the aggregation/significance pipeline. The ApiGateway
/// forwards to these via its HomeStoryController.
/// </summary>
public static class HomeStoryEndpoints
{
    public const string Collection = "home_story";
    private const int DefaultLimit = 60;
    private const int MaxLimit = 730;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);

    /// <summary>A single capability change to render (preview). Device/zone are enriched from the read-models.</summary>
    public record PreviewBeat(
        string CapabilityId,
        string? DeviceId,
        string? Archetype,
        object? OldValue,
        object? NewValue,
        string? TriggerSource,
        string? AdapterSource,
        string? ZoneId,
        string? ZoneName,
        string? ZoneKind,
        string? RuleId,
        string? Mode,
        DateTime? Timestamp);

    /// <summary>Preview request: one or more beats + an optional causal phrase, rendered in the given locale.</summary>
    public record PreviewRequest(string? Locale, List<PreviewBeat> Beats, string? Cause);

    public static void MapHomeStoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/home-story").WithTags("HomeStory").WithOpenApi();

        // GET /api/home-story?from=&to=&limit= — the materialized diary feed (empty until the builder runs).
        group.MapGet("/", async (DateTime? from, DateTime? to, int? limit, IMongoDatabase db) =>
        {
            var hi = to?.ToUniversalTime() ?? DateTime.UtcNow;
            var lo = from?.ToUniversalTime() ?? hi - DefaultWindow;
            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            try
            {
                var b = Builders<HomeStoryEntry>.Filter;
                var entries = await db.GetCollection<HomeStoryEntry>(Collection)
                    .Find(b.And(b.Gte(x => x.Date, lo), b.Lte(x => x.Date, hi)))
                    .SortByDescending(x => x.Date).Limit(take).ToListAsync();
                return Results.Ok(entries);
            }
            catch
            {
                // Collection may not exist yet (builder hasn't run) — the feed is simply empty.
                return Results.Ok(Array.Empty<HomeStoryEntry>());
            }
        });

        // POST /api/home-story/preview — on-the-fly render (Epic 2N Phase 0).
        group.MapPost("/preview", async (
            PreviewRequest req, LanguagePackProvider packs, INarrativeRendererSelector selector,
            IMongoDatabase db, CancellationToken ct) =>
        {
            if (req?.Beats is null || req.Beats.Count == 0)
                return Results.BadRequest(new { error = "at least one beat is required" });

            var locale = string.IsNullOrWhiteSpace(req.Locale) ? "ru" : req.Locale!;
            var beats = new List<Beat>();
            foreach (var pb in req.Beats)
                beats.Add(await BuildBeat(pb, db, ct));

            List<Scene> scenes;
            if (!string.IsNullOrWhiteSpace(req.Cause))
            {
                var actor = beats.FirstOrDefault(x => x.Actor != PersonaRole.Impersonal) ?? beats[0];
                scenes = new List<Scene> { SceneOf(actor, req.Cause) };
            }
            else
            {
                scenes = beats.Select(x => SceneOf(x, null)).ToList();
            }

            var day = new DayStory
            {
                Date = DateOnly.FromDateTime(beats[0].Timestamp),
                TimeZoneId = string.Empty,
                Scenes = scenes,
            };

            var pack = await packs.GetAsync(locale, ct);
            var renderer = selector.ResolveFor(locale);
            var rendered = renderer.Render(day, pack, new NarrativeState()); // preview is stateless

            return Results.Ok(new { paragraph = rendered.Paragraph, locale = renderer.Locale, renderer = rendered.RendererTier });
        });
    }

    private static Scene SceneOf(Beat b, string? cause) => new()
    {
        Id = "preview",
        StartedAt = b.Timestamp,
        EndedAt = b.Timestamp,
        Actor = b.Actor,
        CausalRootKey = "preview",
        Cause = cause,
        Mode = b.Mode,
        Beats = new List<Beat> { b },
    };

    /// <summary>Turn a preview request beat into a resolved <see cref="Beat"/>, enriching archetype/zone from Mongo.</summary>
    private static async Task<Beat> BuildBeat(PreviewBeat pb, IMongoDatabase db, CancellationToken ct)
    {
        string? archetype = pb.Archetype;
        var zoneId = pb.ZoneId ?? string.Empty;
        var zoneName = pb.ZoneName;
        var zoneKind = pb.ZoneKind;
        var adapterSource = pb.AdapterSource;

        if (!string.IsNullOrEmpty(pb.DeviceId))
        {
            var dev = await db.GetCollection<CapabilityDeviceDocument>("capability_devices")
                .Find(d => d.Id == pb.DeviceId).FirstOrDefaultAsync(ct);
            if (dev is not null)
            {
                archetype ??= string.IsNullOrEmpty(dev.Archetype) ? dev.AutoArchetype : dev.Archetype;
                adapterSource ??= dev.AdapterSource;
                if (string.IsNullOrEmpty(zoneId)) zoneId = dev.ZoneId;
            }
        }

        if (!string.IsNullOrEmpty(zoneId) && (zoneName is null || zoneKind is null))
        {
            var zone = await db.GetCollection<Zone>("zones").Find(z => z.Id == zoneId).FirstOrDefaultAsync(ct);
            if (zone is not null)
            {
                zoneName ??= zone.Name;
                zoneKind ??= zone.Kind;
            }
        }

        archetype ??= DeviceArchetypes.Unknown;
        var oldValue = Normalize(pb.OldValue);
        var newValue = Normalize(pb.NewValue);

        return new Beat
        {
            Timestamp = pb.Timestamp ?? DateTime.UtcNow,
            Actor = PersonaResolver.Resolve(pb.TriggerSource, adapterSource),
            ArchetypeKey = archetype,
            CapabilityId = pb.CapabilityId,
            Transition = TransitionResolver.Derive(pb.CapabilityId, oldValue, newValue),
            DeviceId = pb.DeviceId,
            DeviceRef = pb.DeviceId ?? archetype,
            ZoneId = zoneId,
            ZoneName = zoneName,
            ZoneKind = zoneKind,
            OldValue = oldValue,
            NewValue = newValue,
            RuleId = pb.RuleId,
            Mode = pb.Mode,
        };
    }

    /// <summary>Coerce a JSON-bound value (JsonElement) to a plain primitive the resolvers understand.</summary>
    private static object? Normalize(object? v) => v switch
    {
        JsonElement je => je.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => je.TryGetDouble(out var d) ? d : je.GetRawText(),
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => je.GetRawText(),
        },
        _ => v,
    };
}
