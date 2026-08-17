// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Blocks;
using Domovoy.Contracts.Ml;
using Domovoy.Contracts.Proposals;
using Domovoy.DbGateway.Endpoints;
using Domovoy.DbGateway.Models;

using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// ML-task persistence + approval side-effect + model retention against a real Mongo (Epic 2P): the
/// <c>ml_tasks</c> document round-trips (incl. the trainer-owned status subdocument), approving an
/// <see cref="ProposalKind.MlTask"/> proposal creates exactly one task per target, and the registry prune
/// keeps only the N most recent versions of each (kind, target, scope) line without touching neighbours.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")] // needs Docker (Testcontainers); excluded from the unit-only CI job
public sealed class MlTaskLifecycleTests
{
    private readonly InfraFixture _fx;
    public MlTaskLifecycleTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<MlTaskDocument> Tasks => _fx.Db.GetCollection<MlTaskDocument>(MlTaskEndpoints.Collection);
    private IMongoCollection<MlModelDocument> Models => _fx.Db.GetCollection<MlModelDocument>(MlEndpoints.Collection);

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task MlTaskDocument_RoundTrips_WithStatus()
    {
        var doc = new MlTaskDocument
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Кухня: температура",
            TargetCapability = "temperature",
            TargetKey = "temperature",
            WindowDays = 14,
            MinSamples = 50,
            ClampMin = 16,
            ClampMax = 26,
            Status = new MlTaskStatus
            {
                LastTrainAt = DateTime.UtcNow,
                LastTrainOk = false,
                LastMessage = "not enough data (12/50)",
                LastSampleCount = 12,
            },
        };
        await Tasks.InsertOneAsync(doc);

        var stored = await Tasks.Find(x => x.Id == doc.Id).FirstOrDefaultAsync();

        Assert.Equal("Кухня: температура", stored.Name);
        Assert.Equal(14, stored.WindowDays);
        Assert.Equal(26, stored.ClampMax);
        Assert.NotNull(stored.Status);
        Assert.False(stored.Status!.LastTrainOk);
        Assert.Equal("not enough data (12/50)", stored.Status.LastMessage);
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task ApproveMlTaskProposal_CreatesTask_OncePerTarget()
    {
        var target = $"cap_{Guid.NewGuid():N}";
        var proposal = new Proposal { Kind = ProposalKind.MlTask, MlTaskTarget = target, Title = $"learn {target}" };

        var (ok, error) = await ProposalApplication.ApplyAsync(_fx.Db, proposal);

        Assert.True(ok, error);
        var created = await Tasks.Find(x => x.TargetKey == target.ToLowerInvariant()).FirstOrDefaultAsync();
        Assert.NotNull(created);
        Assert.Equal(target, created.TargetCapability);
        Assert.True(created.Enabled);

        // Approving a second proposal for the same target must fail, not duplicate the task.
        var (okAgain, errorAgain) = await ProposalApplication.ApplyAsync(_fx.Db, proposal);
        Assert.False(okAgain);
        Assert.NotNull(errorAgain);
        Assert.Equal(1, await Tasks.CountDocumentsAsync(x => x.TargetKey == target.ToLowerInvariant()));
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task ApproveMlTaskProposal_WithoutTarget_ReportsError()
    {
        var (ok, error) = await ProposalApplication.ApplyAsync(
            _fx.Db, new Proposal { Kind = ProposalKind.MlTask, Title = "no target" });

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task Prune_KeepsNewestVersionsPerLine_AndLeavesNeighboursAlone()
    {
        var target = $"cap_{Guid.NewGuid():N}";
        MlModelDocument Doc(int version, ModelScope scope, string kind = MlModelKinds.ScheduleRegression) => new()
        {
            Id = Guid.NewGuid().ToString(), Name = $"{target} v{version}", Kind = kind,
            TargetCapability = target, Scope = scope, Version = version, Artifact = new byte[] { 1 },
        };

        // One line with 5 versions + a neighbour line (zone scope) with 2 — the neighbour must survive intact.
        var global = Enumerable.Range(1, 5).Select(v => Doc(v, ModelScope.Global)).ToList();
        var zone = Enumerable.Range(1, 2).Select(v => Doc(v, ModelScope.Zone("kitchen"))).ToList();
        await Models.InsertManyAsync(global.Concat(zone));

        var filter = Builders<MlModelDocument>.Filter.Eq(x => x.TargetCapability, target);
        var deleted = await MlEndpoints.PruneAsync(_fx.Db, filter, keepLast: 3);

        Assert.Equal(2, deleted); // global v1+v2 pruned
        var left = await Models.Find(filter).ToListAsync();
        var globalLeft = left.Where(m => m.Scope!.Level == ModelScopeLevels.Global).Select(m => m.Version).OrderBy(v => v).ToList();
        var zoneLeft = left.Where(m => m.Scope!.Level == ModelScopeLevels.Zone).Select(m => m.Version).OrderBy(v => v).ToList();
        Assert.Equal(new[] { 3, 4, 5 }, globalLeft);
        Assert.Equal(new[] { 1, 2 }, zoneLeft);
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task Prune_KeepsAVersionPinnedByAControlBlock()
    {
        var target = $"cap_{Guid.NewGuid():N}";
        MlModelDocument Doc(int version) => new()
        {
            Id = Guid.NewGuid().ToString(), Name = $"{target} v{version}", Kind = MlModelKinds.ScheduleRegression,
            TargetCapability = target, Scope = ModelScope.Global, Version = version, Artifact = new byte[] { 1 },
        };
        await Models.InsertManyAsync(Enumerable.Range(1, 5).Select(Doc));

        // A governor block pins v2 (Epic 2C model_selection — a person's decision) and names its ML target
        // through its measured input. Retention must not delete v2 behind their back, even though keepLast: 3
        // would otherwise expire it.
        var blocks = _fx.Db.GetCollection<ControlBlock>(BlockEndpoints.Collection);
        var block = new ControlBlock
        {
            Id = Guid.NewGuid().ToString(),
            Name = "pinned governor",
            TypeId = "ml_thermostat",
            Params = new Dictionary<string, double> { ["stage"] = 1, [MlEndpoints.ModelVersionParam] = 2 },
            Inputs = new Dictionary<string, PortBinding>
            {
                [target] = new() { DeviceId = Guid.NewGuid().ToString(), CapabilityId = target },
            },
        };
        await blocks.InsertOneAsync(block);

        try
        {
            var filter = Builders<MlModelDocument>.Filter.Eq(x => x.TargetCapability, target);
            var deleted = await MlEndpoints.PruneAsync(_fx.Db, filter, keepLast: 3);

            Assert.Equal(1, deleted); // v1 expired and unpinned; v2 expired but pinned → kept
            var left = (await Models.Find(filter).ToListAsync()).Select(m => m.Version).OrderBy(v => v).ToList();
            Assert.Equal(new[] { 2, 3, 4, 5 }, left);
        }
        finally
        {
            await blocks.DeleteOneAsync(b => b.Id == block.Id);
        }
    }
}
