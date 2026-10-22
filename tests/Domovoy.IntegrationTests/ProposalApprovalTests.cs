// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Blocks;
using Domovoy.Contracts.Proposals;
using Domovoy.Contracts.Scenes;
using Domovoy.DbGateway.Endpoints;

using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Verifies the approval side-effects (roadmap Epic 2C) against a real Mongo: approving a proposal mutates the
/// right target document. Exercises <see cref="ProposalApplication.ApplyAsync"/> — the same logic the approve
/// endpoint runs — so the BSON round-trip of the patched rule/block is covered too.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")] // needs Docker (Testcontainers); excluded from the unit-only CI job
public sealed class ProposalApprovalTests
{
    private readonly InfraFixture _fx;
    public ProposalApprovalTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<AutomationRule> Rules => _fx.Db.GetCollection<AutomationRule>(AutomationEndpoints.Collection);
    private IMongoCollection<ControlBlock> Blocks => _fx.Db.GetCollection<ControlBlock>(BlockEndpoints.Collection);
    private IMongoCollection<Scene> ScenesColl => _fx.Db.GetCollection<Scene>(SceneEndpoints.Collection);

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task ApproveRule_SetsCandidateRuleActive()
    {
        var rule = new AutomationRule { Id = Guid.NewGuid().ToString(), Name = "candidate", Status = RuleStatus.Proposed };
        await Rules.InsertOneAsync(rule);
        var proposal = new Proposal { Kind = ProposalKind.Rule, RuleId = rule.Id, Title = "activate candidate" };

        var (ok, error) = await ProposalApplication.ApplyAsync(_fx.Db, proposal);

        Assert.True(ok, error);
        var stored = await Rules.Find(x => x.Id == rule.Id).FirstOrDefaultAsync();
        Assert.Equal(RuleStatus.Active, stored.Status);
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task ApproveBlockPromotion_PatchesStageParam()
    {
        var block = new ControlBlock
        {
            Id = Guid.NewGuid().ToString(), Name = "ml gov", TypeId = "ml_thermostat",
            Params = new Dictionary<string, double> { ["stage"] = 0 },
        };
        await Blocks.InsertOneAsync(block);
        var proposal = new Proposal { Kind = ProposalKind.BlockPromotion, BlockId = block.Id, ToStage = 1, Title = "promote" };

        var (ok, error) = await ProposalApplication.ApplyAsync(_fx.Db, proposal);

        Assert.True(ok, error);
        var stored = await Blocks.Find(x => x.Id == block.Id).FirstOrDefaultAsync();
        Assert.Equal(1d, stored.Params["stage"]);
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task ApproveModelSelection_PatchesModelVersionParam()
    {
        var block = new ControlBlock
        {
            Id = Guid.NewGuid().ToString(), Name = "ml gov", TypeId = "ml_thermostat",
            Params = new Dictionary<string, double>(),
        };
        await Blocks.InsertOneAsync(block);
        var proposal = new Proposal { Kind = ProposalKind.ModelSelection, BlockId = block.Id, ModelVersion = 5, Title = "pin v5" };

        var (ok, error) = await ProposalApplication.ApplyAsync(_fx.Db, proposal);

        Assert.True(ok, error);
        var stored = await Blocks.Find(x => x.Id == block.Id).FirstOrDefaultAsync();
        Assert.Equal(5d, stored.Params["model_version"]);
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task ApproveScene_CreatesSceneAndBundledScheduleRule()
    {
        var sceneName = $"Evening-{Guid.NewGuid():N}";
        var proposal = new Proposal
        {
            Kind = ProposalKind.Scene,
            Title = "new scene",
            SceneScheduleCron = "0 20 * * *",
            SceneDraft = new Scene
            {
                Name = sceneName,
                Targets =
                {
                    new SceneTarget { DeviceId = "lampA", Set = new() { ["on_off"] = true, ["brightness"] = 40 } },
                    new SceneTarget { DeviceId = "lampB", Set = new() { ["on_off"] = true } },
                },
            },
        };

        var (ok, error) = await ProposalApplication.ApplyAsync(_fx.Db, proposal);

        Assert.True(ok, error);
        var scene = await ScenesColl.Find(x => x.Name == sceneName).FirstOrDefaultAsync();
        Assert.NotNull(scene);
        Assert.NotEqual(string.Empty, scene.Id);
        Assert.Equal(2, scene.Targets.Count);

        // The bundled schedule cron → an Active rule that activates the freshly-created scene daily.
        var rules = await Rules.Find(FilterDefinition<AutomationRule>.Empty).ToListAsync();
        var rule = Assert.Single(rules, r => r.Actions.Any(a => a.Type == ActionType.Scene && a.SceneId == scene.Id));
        Assert.Equal(RuleStatus.Active, rule.Status);
        Assert.Equal(TriggerType.Time, rule.Triggers[0].Type);
        Assert.Equal("0 20 * * *", rule.Triggers[0].Cron);
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task ApproveRuleAmendment_DisablesTheRule()
    {
        // Epic 3J "living rules": approving a "retire this rule" amendment disables the (live) rule it targets.
        var rule = new AutomationRule { Id = Guid.NewGuid().ToString(), Name = "overridden", Status = RuleStatus.Active };
        await Rules.InsertOneAsync(rule);
        var proposal = new Proposal
        {
            Kind = ProposalKind.RuleAmendment, RuleId = rule.Id, AmendmentAction = "disable", Title = "retire overridden",
        };

        var (ok, error) = await ProposalApplication.ApplyAsync(_fx.Db, proposal);

        Assert.True(ok, error);
        var stored = await Rules.Find(x => x.Id == rule.Id).FirstOrDefaultAsync();
        Assert.Equal(RuleStatus.Disabled, stored.Status);
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task Approve_MissingTarget_ReportsError()
    {
        var proposal = new Proposal { Kind = ProposalKind.Rule, RuleId = "does-not-exist", Title = "orphan" };

        var (ok, error) = await ProposalApplication.ApplyAsync(_fx.Db, proposal);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
