// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Common.Logging;
using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Services;

using MongoDB.Bson;
using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Activity Center (roadmap Epic 2G): a single readable/searchable/filterable feed that merges, at the
/// query layer, three sources kept separate in storage — device events (P0-5 <c>device_events</c>),
/// automation runs (<c>auto_history</c>) and operational logs (Serilog Mongo sink <c>ops_logs</c>). The
/// answer to "what happened and why" in one place. Domain data and ops logs stay separate at rest (P0-5);
/// they are only unified here.
/// </summary>
public static class ActivityEndpoints
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 2000;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    public const string SourceDevice = "device";
    public const string SourceAutomation = "automation";
    public const string SourceBlock = "block";
    public const string SourceSystem = "system";

    /// <summary>
    /// One unified activity row for the client. <c>TriggerKind/TriggerId/TriggerName</c> carry the concrete
    /// initiator ("who exactly did it"): kind is the coarse bucket (user/rule/block/device/presence/ml),
    /// id is the actor's id (rule id, block id, device id, user id) and name is resolved server-side so the
    /// UI can render a clickable "by …" chip that navigates to the actor.
    /// </summary>
    public record ActivityEntry(
        DateTime Timestamp, string Source, string Severity, string Title,
        string? Detail, string? DeviceId, string? Kind, string? Service,
        string? TriggerKind = null, string? TriggerId = null, string? TriggerName = null);

    public static void MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Activity").WithOpenApi();

        // GET /api/activity?source=&severity=&deviceId=&from=&to=&q=&limit=
        group.MapGet("/activity", async (
            string? source, string? severity, string? deviceId, string? q,
            DateTime? from, DateTime? to, int? limit, IMongoDatabase db) =>
        {
            var hi = to?.ToUniversalTime() ?? DateTime.UtcNow;
            var lo = from?.ToUniversalTime() ?? hi - DefaultWindow;
            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var want = (string? s) => source is null || string.Equals(source, s, StringComparison.OrdinalIgnoreCase);

            var entries = new List<ActivityEntry>();
            if (want(SourceDevice)) entries.AddRange(await DeviceEntries(db, lo, hi, deviceId, take));
            if (want(SourceAutomation)) entries.AddRange(await AutomationEntries(db, lo, hi, take));
            if (want(SourceBlock) && deviceId is null) entries.AddRange(await BlockEntries(db, lo, hi, take));
            if (want(SourceSystem)) entries.AddRange(await SystemEntries(db, lo, hi, take));

            IEnumerable<ActivityEntry> result = entries;
            if (!string.IsNullOrWhiteSpace(severity))
                result = result.Where(e => string.Equals(e.Severity, severity, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                result = result.Where(e =>
                    (e.Title?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Detail?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Service?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return Results.Ok(result.OrderByDescending(e => e.Timestamp).Take(take));
        });
    }

    private static async Task<IEnumerable<ActivityEntry>> DeviceEntries(
        IMongoDatabase db, DateTime lo, DateTime hi, string? deviceId, int take)
    {
        var b = Builders<DeviceEventLog>.Filter;
        var filters = new List<FilterDefinition<DeviceEventLog>> { b.Gte(x => x.Timestamp, lo), b.Lte(x => x.Timestamp, hi) };
        if (!string.IsNullOrEmpty(deviceId)) filters.Add(b.Eq(x => x.Meta.DeviceId, deviceId));

        var docs = await db.GetCollection<DeviceEventLog>(TimeSeriesInitializer.DeviceEventsCollection)
            .Find(b.And(filters)).SortByDescending(x => x.Timestamp).Limit(take).ToListAsync();

        var names = await ResolveTriggerNames(db, docs);

        return docs.Select(d =>
        {
            names.TryGetValue((d.TriggerSource, d.TriggerId ?? ""), out var triggerName);
            return new ActivityEntry(
                d.Timestamp, SourceDevice, "info",
                Title: d.Meta.Kind switch
                {
                    EventKinds.Command => $"command {d.CapabilityId} = {Val(d.NewValue)}",
                    EventKinds.ModeChange => $"mode → {Val(d.NewValue)}",
                    _ => $"{d.CapabilityId}: {Val(d.OldValue)} → {Val(d.NewValue)}",
                },
                Detail: $"by {d.TriggerSource}" + (triggerName is null ? "" : $" ({triggerName})"),
                DeviceId: d.Meta.DeviceId, Kind: d.Meta.Kind, Service: null,
                TriggerKind: d.TriggerSource, TriggerId: d.TriggerId, TriggerName: triggerName);
        });
    }

    /// <summary>
    /// Batch-resolve initiator ids to display names — one <c>$in</c> query per kind: rules from
    /// <c>automations</c>, blocks from <c>control_blocks</c>, devices (incl. presence sensors) from
    /// <c>capability_devices</c>, users (self-declared, Epic 2E) from <c>users</c>.
    /// </summary>
    private static async Task<Dictionary<(string Kind, string Id), string>> ResolveTriggerNames(
        IMongoDatabase db, IReadOnlyList<DeviceEventLog> docs)
    {
        var result = new Dictionary<(string, string), string>();

        async Task Resolve<T>(
            string kind, string altKind, string collection,
            System.Linq.Expressions.Expression<Func<T, string>> idField, Func<T, string> id, Func<T, string> name)
        {
            var ids = docs
                .Where(d => (d.TriggerSource == kind || d.TriggerSource == altKind) && !string.IsNullOrEmpty(d.TriggerId))
                .Select(d => d.TriggerId!).Distinct().ToList();
            if (ids.Count == 0) return;

            var found = await db.GetCollection<T>(collection)
                .Find(Builders<T>.Filter.In(idField, ids)).ToListAsync();
            foreach (var doc in found)
            {
                result[(kind, id(doc))] = name(doc);
                if (altKind.Length > 0) result[(altKind, id(doc))] = name(doc);
            }
        }

        await Resolve<Contracts.Automations.AutomationRule>(
            TriggerSources.Rule, "", AutomationEndpoints.Collection, x => x.Id, x => x.Id, x => x.Name);
        await Resolve<Contracts.Blocks.ControlBlock>(
            TriggerSources.Block, "", BlockEndpoints.Collection, x => x.Id, x => x.Id, x => x.Name);
        await Resolve<CapabilityDeviceDocument>(
            TriggerSources.Presence, TriggerSources.Device, CapabilityDeviceEndpoints.Collection,
            x => x.Id, x => x.Id, x => x.Name);
        await Resolve<Contracts.Security.User>(
            TriggerSources.User, "", UsersEndpoints.Collection, x => x.Id, x => x.Id, x => x.DisplayName);

        return result;
    }

    private static async Task<IEnumerable<ActivityEntry>> AutomationEntries(
        IMongoDatabase db, DateTime lo, DateTime hi, int take)
    {
        var b = Builders<AutoHistory>.Filter;
        var docs = await db.GetCollection<AutoHistory>(AutomationEndpoints.HistoryCollection)
            .Find(b.And(b.Gte(x => x.Timestamp, lo), b.Lte(x => x.Timestamp, hi)))
            .SortByDescending(x => x.Timestamp).Limit(take).ToListAsync();

        return docs.Select(h => new ActivityEntry(
            h.Timestamp, SourceAutomation,
            Severity: !h.ConditionsMet ? "info" : (h.Success ? "info" : "error"),
            Title: $"{h.RuleName}: " + (!h.ConditionsMet ? "skipped" : (h.Success ? $"ran ({h.ActionsExecuted})" : "failed")),
            Detail: string.Join(" — ", new[] { h.TriggerSummary, h.Detail }.Where(s => !string.IsNullOrEmpty(s))),
            DeviceId: null, Kind: "automation", Service: null,
            TriggerKind: TriggerSources.Rule, TriggerId: h.RuleId, TriggerName: h.RuleName));
    }

    private static async Task<IEnumerable<ActivityEntry>> BlockEntries(
        IMongoDatabase db, DateTime lo, DateTime hi, int take)
    {
        var b = Builders<BlockHistory>.Filter;
        var docs = await db.GetCollection<BlockHistory>(BlockHistory.Collection)
            .Find(b.And(b.Gte(x => x.Timestamp, lo), b.Lte(x => x.Timestamp, hi)))
            .SortByDescending(x => x.Timestamp).Limit(take).ToListAsync();

        return docs.Select(h => new ActivityEntry(
            h.Timestamp, SourceBlock,
            Severity: h.Ok ? "info" : "error",
            Title: $"{h.BlockName}: {h.Summary}",
            Detail: h.Ok ? h.TypeId : h.Detail,
            DeviceId: null, Kind: "block", Service: null,
            TriggerKind: TriggerSources.Block, TriggerId: h.BlockId, TriggerName: h.BlockName));
    }

    private static async Task<IEnumerable<ActivityEntry>> SystemEntries(
        IMongoDatabase db, DateTime lo, DateTime hi, int take)
    {
        try
        {
            var b = Builders<BsonDocument>.Filter;
            var docs = await db.GetCollection<BsonDocument>(SerilogBootstrap.OpsLogCollection)
                .Find(b.And(b.Gte("Timestamp", lo), b.Lte("Timestamp", hi)))
                .SortByDescending(x => x["Timestamp"]).Limit(take).ToListAsync();

            return docs.Select(d => new ActivityEntry(
                Timestamp: d.TryGetValue("Timestamp", out var ts) && ts.IsValidDateTime ? ts.ToUniversalTime() : DateTime.UtcNow,
                Source: SourceSystem,
                Severity: MapLevel(Str(d, "Level")),
                Title: Str(d, "RenderedMessage", "MessageTemplate"),
                Detail: d.TryGetValue("Exception", out var ex) && !ex.IsBsonNull ? ex.AsString : null,
                DeviceId: null, Kind: "log",
                Service: d.TryGetValue("Properties", out var p) && p.IsBsonDocument && p.AsBsonDocument.TryGetValue("Service", out var svc) && !svc.IsBsonNull ? svc.ToString() : null));
        }
        catch
        {
            // ops_logs may not exist yet (sink not configured / nothing logged) — system source is optional.
            return Array.Empty<ActivityEntry>();
        }
    }

    private static string MapLevel(string? level) => level switch
    {
        "Error" or "Fatal" => "error",
        "Warning" => "warn",
        _ => "info",
    };

    private static string Str(BsonDocument d, params string[] keys)
    {
        foreach (var k in keys)
            if (d.TryGetValue(k, out var v) && !v.IsBsonNull) return v.ToString() ?? string.Empty;
        return string.Empty;
    }

    private static string Val(object? v) => v switch
    {
        null => "—",
        bool b => b ? "on" : "off",
        _ => v.ToString() ?? string.Empty,
    };
}
