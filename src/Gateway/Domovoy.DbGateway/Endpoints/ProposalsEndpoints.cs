// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Blocks;
using Domovoy.Contracts.Proposals;
using Domovoy.Contracts.Scenes;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// The approval queue (roadmap Epic 2C) in the <c>proposals</c> collection. DbGateway owns all three target
/// collections (<c>automations</c>, <c>control_blocks</c>, <c>ml_models</c>), so approving a proposal — mutating
/// the target and marking the proposal <see cref="ProposalStatus.Approved"/> — is one operation in one database;
/// the AutomationService then picks the change up through its <c>RefreshLoop</c>. No new service is introduced.
/// The WebUI manages the queue via the ApiGateway proxy; the 2C heuristic proposer and later the discovery
/// engine (2F) create proposals here too. <b>Invariant:</b> nothing activates without a person — reject never
/// touches the target.
/// </summary>
public static class ProposalsEndpoints
{
    public const string Collection = "proposals";

    public static void MapProposalsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/proposals").WithTags("Proposals").WithOpenApi();

        // Queue, newest first; optional ?status= and ?kind= filters (WebUI defaults to Proposed).
        group.MapGet("/", async (string? status, string? kind, IMongoDatabase db) =>
        {
            var b = Builders<Proposal>.Filter;
            var filters = new List<FilterDefinition<Proposal>>();
            if (Enum.TryParse<ProposalStatus>(status, ignoreCase: true, out var s)) filters.Add(b.Eq(x => x.Status, s));
            if (Enum.TryParse<ProposalKind>(kind, ignoreCase: true, out var k)) filters.Add(b.Eq(x => x.Kind, k));
            var filter = filters.Count == 0 ? FilterDefinition<Proposal>.Empty : b.And(filters);

            var rows = await Proposals(db).Find(filter).SortByDescending(x => x.CreatedAt).ToListAsync();
            return Results.Ok(rows);
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var p = await Proposals(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return p is null ? Results.NotFound() : Results.Ok(p);
        });

        // Create a proposal (from the heuristic proposer, the WebUI, or later 2F). Always lands as Proposed.
        group.MapPost("/", async (Proposal proposal, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(proposal.Title))
                return Results.BadRequest(new { error = "proposal title is required" });

            // HTTP binding leaves a scene draft's target values as JsonElement, which the Mongo ObjectSerializer
            // refuses; flatten to BCL primitives at the boundary (same fix as SceneEndpoints/AutomationEndpoints).
            if (proposal.SceneDraft is not null) SceneEndpoints.NormalizeJsonValues(proposal.SceneDraft);

            proposal.Id = Guid.NewGuid().ToString();
            proposal.Status = ProposalStatus.Proposed;
            proposal.DecisionId = string.Empty;
            proposal.DecidedAt = null;
            proposal.CreatedAt = DateTime.UtcNow;
            await Proposals(db).InsertOneAsync(proposal);
            return Results.Created($"/api/proposals/{proposal.Id}", proposal);
        });

        // Approve: apply the kind-specific side-effect on the target, then mark the proposal Approved with a
        // fresh DecisionId (the provenance handle for the activated action). Atomic-enough for a homelab: both
        // writes hit the same database; if the side-effect fails we leave the proposal Proposed and report why.
        group.MapPost("/{id}/approve", async (string id, IMongoDatabase db) =>
        {
            var p = await Proposals(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (p is null) return Results.NotFound();
            if (p.Status != ProposalStatus.Proposed)
                return Results.Conflict(new { error = $"proposal is already {p.Status}" });

            var decisionId = Guid.NewGuid().ToString();
            var applied = await ProposalApplication.ApplyAsync(db, p);
            if (!applied.Ok) return Results.BadRequest(new { error = applied.Error });

            var update = Builders<Proposal>.Update
                .Set(x => x.Status, ProposalStatus.Approved)
                .Set(x => x.DecisionId, decisionId)
                .Set(x => x.DecidedAt, DateTime.UtcNow);
            await Proposals(db).UpdateOneAsync(x => x.Id == id, update);

            p.Status = ProposalStatus.Approved;
            p.DecisionId = decisionId;
            p.DecidedAt = DateTime.UtcNow;
            return Results.Ok(p);
        });

        group.MapPost("/{id}/reject", async (string id, IMongoDatabase db) =>
        {
            var p = await Proposals(db).Find(x => x.Id == id && x.Status == ProposalStatus.Proposed).FirstOrDefaultAsync();
            if (p is null) return Results.NotFound();

            var update = Builders<Proposal>.Update
                .Set(x => x.Status, ProposalStatus.Rejected)
                .Set(x => x.DecidedAt, DateTime.UtcNow);
            var result = await Proposals(db).UpdateOneAsync(x => x.Id == id && x.Status == ProposalStatus.Proposed, update);
            if (result.MatchedCount == 0) return Results.NotFound();

            // Epic 3I: a Rule-kind proposal carries a candidate rule created as Proposed (by the 2C/2F proposers).
            // Rejecting the proposal must remove that orphan so it neither lingers in `automations` nor silently
            // suppresses re-discovery via the RuleAlreadyWires dedup. Only ever deletes a still-Proposed rule —
            // an approved (Active) rule is never touched. The rejection itself is remembered on the proposal
            // (its title/target dedups future scans).
            if (p.Kind == ProposalKind.Rule && !string.IsNullOrEmpty(p.RuleId))
            {
                await db.GetCollection<AutomationRule>(AutomationEndpoints.Collection)
                    .DeleteOneAsync(x => x.Id == p.RuleId && x.Status == RuleStatus.Proposed);
            }

            return Results.NoContent();
        });
    }

    private static IMongoCollection<Proposal> Proposals(IMongoDatabase db) =>
        db.GetCollection<Proposal>(Collection);
}

/// <summary>
/// The kind-specific side-effect an approval applies to its target collection (roadmap Epic 2C). Extracted
/// from the endpoint so it can be exercised directly against a real Mongo in tests. Every branch is a single
/// mutation in the same database the queue lives in.
/// </summary>
public static class ProposalApplication
{
    /// <summary>Apply the proposal's side-effect; returns (false, reason) if the target is missing/invalid.</summary>
    public static Task<(bool Ok, string? Error)> ApplyAsync(IMongoDatabase db, Proposal p) => p.Kind switch
    {
        ProposalKind.Rule => ApplyRuleAsync(db, p),
        ProposalKind.BlockPromotion => ApplyBlockParamAsync(db, p, "stage", p.ToStage),
        ProposalKind.ModelSelection => ApplyBlockParamAsync(db, p, MlEndpoints.ModelVersionParam, p.ModelVersion),
        ProposalKind.MlTask => ApplyMlTaskAsync(db, p),
        ProposalKind.Scene => ApplySceneAsync(db, p),
        ProposalKind.RuleAmendment => ApplyRuleAmendmentAsync(db, p),
        _ => Task.FromResult<(bool, string?)>((false, "unknown proposal kind")),
    };

    // Scene proposal (Epic 2F × 3B): approving materializes the discovered scene in the scenes collection and,
    // when the proposal carries a schedule cron, also stands up an Active rule that activates it daily. The scene
    // did not exist before approval (like an ML task) — so nothing showed up on /scenes or a dashboard prematurely.
    private static async Task<(bool Ok, string? Error)> ApplySceneAsync(IMongoDatabase db, Proposal p)
    {
        if (p.SceneDraft is null || p.SceneDraft.Targets.Count == 0) return (false, "proposal has no scene draft");

        var scene = p.SceneDraft;
        SceneEndpoints.NormalizeJsonValues(scene); // defensive: values are already primitives once read from BSON
        scene.Id = Guid.NewGuid().ToString();
        scene.CreatedAt = scene.UpdatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(scene.Name)) scene.Name = "Scene";
        await db.GetCollection<Scene>(SceneEndpoints.Collection).InsertOneAsync(scene);

        if (!string.IsNullOrWhiteSpace(p.SceneScheduleCron))
        {
            var rule = new AutomationRule
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"{scene.Name} (scheduled)",
                Description = "Created alongside a discovered scene (Epic 2F × 3B).",
                Status = RuleStatus.Active,
                Triggers = { new RuleTrigger { Type = TriggerType.Time, Cron = p.SceneScheduleCron } },
                Actions = { new RuleAction { Type = ActionType.Scene, SceneId = scene.Id } },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            await db.GetCollection<AutomationRule>(AutomationEndpoints.Collection).InsertOneAsync(rule);
        }
        return (true, null);
    }

    // ML-task proposal (Epic 2P): approving creates the training task with defaults — the user tunes window/
    // clamps later on the ML hub. One task per target: an already-existing task fails the approve with a reason.
    private static async Task<(bool Ok, string? Error)> ApplyMlTaskAsync(IMongoDatabase db, Proposal p)
    {
        if (string.IsNullOrWhiteSpace(p.MlTaskTarget)) return (false, "proposal has no mlTaskTarget");

        var target = p.MlTaskTarget.Trim();
        var tasks = db.GetCollection<Models.MlTaskDocument>(MlTaskEndpoints.Collection);
        var targetKey = target.ToLowerInvariant();
        if (await tasks.Find(x => x.TargetKey == targetKey).AnyAsync())
            return (false, $"a task for '{target}' already exists");

        await tasks.InsertOneAsync(new Models.MlTaskDocument
        {
            Id = Guid.NewGuid().ToString(),
            Name = target,
            TargetCapability = target,
            TargetKey = targetKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        return (true, null);
    }

    // Rule proposal: flip the candidate rule (Proposed) to Active so the engine starts executing it.
    private static async Task<(bool Ok, string? Error)> ApplyRuleAsync(IMongoDatabase db, Proposal p)
    {
        if (string.IsNullOrEmpty(p.RuleId)) return (false, "proposal has no ruleId");
        var rules = db.GetCollection<AutomationRule>(AutomationEndpoints.Collection);
        var update = Builders<AutomationRule>.Update
            .Set(x => x.Status, RuleStatus.Active)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        var result = await rules.UpdateOneAsync(x => x.Id == p.RuleId, update);
        return result.MatchedCount == 0 ? (false, "target rule not found") : (true, null);
    }

    // Rule-amendment proposal (Epic 3J "living rules"): apply a change to an existing rule the household keeps
    // overriding. Actions: "disable" (retire a rule fought everywhere), "add_condition" (add an exception so a
    // rule fought in one context stops misfiring there — self-correcting), "set_threshold" (shift a drifted numeric
    // trigger value). A rule already gone is a harmless error; nothing here activates without the approval itself.
    private static async Task<(bool Ok, string? Error)> ApplyRuleAmendmentAsync(IMongoDatabase db, Proposal p)
    {
        if (string.IsNullOrEmpty(p.RuleId)) return (false, "proposal has no ruleId");
        var rules = db.GetCollection<AutomationRule>(AutomationEndpoints.Collection);

        switch ((p.AmendmentAction ?? string.Empty).ToLowerInvariant())
        {
            case "disable":
            {
                var update = Builders<AutomationRule>.Update
                    .Set(x => x.Status, RuleStatus.Disabled)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow);
                var result = await rules.UpdateOneAsync(x => x.Id == p.RuleId, update);
                return result.MatchedCount == 0 ? (false, "target rule not found") : (true, null);
            }

            case "add_condition":
            {
                if (p.AmendmentCondition is null) return (false, "amendment has no condition");
                var rule = await rules.Find(x => x.Id == p.RuleId).FirstOrDefaultAsync();
                if (rule is null) return (false, "target rule not found");
                rule.Conditions.Add(p.AmendmentCondition);
                rule.UpdatedAt = DateTime.UtcNow;
                await rules.ReplaceOneAsync(x => x.Id == p.RuleId, rule);
                return (true, null);
            }

            case "set_threshold":
            {
                if (string.IsNullOrEmpty(p.AmendmentCapabilityId) || p.AmendmentValue is null)
                    return (false, "amendment has no capability/value");
                var rule = await rules.Find(x => x.Id == p.RuleId).FirstOrDefaultAsync();
                if (rule is null) return (false, "target rule not found");

                var changed = 0;
                foreach (var trg in rule.Triggers)
                    if (trg.Type == TriggerType.DeviceState
                        && string.Equals(trg.CapabilityId, p.AmendmentCapabilityId, StringComparison.OrdinalIgnoreCase)
                        && IsNumeric(trg.Value))
                    { trg.Value = p.AmendmentValue.Value; changed++; }

                if (changed == 0) return (false, "no matching numeric trigger threshold to set");
                rule.UpdatedAt = DateTime.UtcNow;
                await rules.ReplaceOneAsync(x => x.Id == p.RuleId, rule);
                return (true, null);
            }

            default:
                return (false, $"unsupported amendment action '{p.AmendmentAction}'");
        }
    }

    private static bool IsNumeric(object? value) =>
        value is double or float or long or int or decimal;

    // Block-targeting proposals (promotion / model pin): patch one numeric param on the block. Params is
    // numeric-only by contract (Epic 1H), which is exactly what a stage index or a model version needs.
    private static async Task<(bool Ok, string? Error)> ApplyBlockParamAsync(
        IMongoDatabase db, Proposal p, string key, int? value)
    {
        if (string.IsNullOrEmpty(p.BlockId)) return (false, "proposal has no blockId");
        if (value is null) return (false, $"proposal has no {key} value");

        var blocks = db.GetCollection<ControlBlock>(BlockEndpoints.Collection);
        var block = await blocks.Find(x => x.Id == p.BlockId).FirstOrDefaultAsync();
        if (block is null) return (false, "target block not found");

        block.Params[key] = value.Value;
        block.UpdatedAt = DateTime.UtcNow;
        await blocks.ReplaceOneAsync(x => x.Id == p.BlockId, block);
        return (true, null);
    }
}
