// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.Contracts.Scenes;
using Domovoy.DbGateway.Endpoints;

using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Scenes (roadmap Epic 3B) persist target capability values the same way rules do: a scene POSTed over
/// HTTP carries its target values as <see cref="JsonElement"/>, which the Mongo <c>ObjectSerializer</c>
/// refuses to persist. <see cref="SceneEndpoints.NormalizeJsonValues"/> must flatten them to BCL
/// primitives that round-trip through a real Mongo — the same contract as
/// <see cref="AutomationRuleNormalizationTests"/> for rules.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")] // needs Docker (Testcontainers); excluded from the unit-only CI job
public sealed class SceneNormalizationTests
{
    private readonly InfraFixture _fx;
    public SceneNormalizationTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<Scene> Scenes =>
        _fx.Db.GetCollection<Scene>(SceneEndpoints.Collection);

    private static object? Json(string literal) => JsonSerializer.Deserialize<object>(literal);

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task HttpBoundScene_WithJsonElementValues_RoundTripsThroughMongo()
    {
        // Values exactly as ASP.NET model binding produces them from a Re-Capture POST: JsonElement.
        var scene = new Scene
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Movie night",
            Description = "Dim the living room",
            Targets =
            {
                new SceneTarget
                {
                    DeviceId = Guid.NewGuid().ToString(),
                    Set = new Dictionary<string, object?> { ["on_off"] = Json("true"), ["brightness"] = Json("20") },
                },
                new SceneTarget
                {
                    DeviceId = Guid.NewGuid().ToString(),
                    Set = new Dictionary<string, object?> { ["on_off"] = Json("false") },
                },
            },
        };

        SceneEndpoints.NormalizeJsonValues(scene);
        await Scenes.InsertOneAsync(scene); // would throw BsonSerializationException without normalization

        var stored = await Scenes.Find(x => x.Id == scene.Id).FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal(2, stored.Targets.Count);
        Assert.Equal(true, stored.Targets[0].Set["on_off"]);
        Assert.Equal(20d, Convert.ToDouble(stored.Targets[0].Set["brightness"]));
        Assert.Equal(false, stored.Targets[1].Set["on_off"]);
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task Scene_ListedBack_PreservesTargets()
    {
        var scene = new Scene
        {
            Id = Guid.NewGuid().ToString(),
            Name = "All off",
            Targets =
            {
                new SceneTarget
                {
                    DeviceId = Guid.NewGuid().ToString(),
                    Set = new Dictionary<string, object?> { ["on_off"] = false },
                },
            },
        };

        await Scenes.InsertOneAsync(scene);

        var all = await Scenes.Find(FilterDefinition<Scene>.Empty).ToListAsync();
        var found = Assert.Single(all, s => s.Id == scene.Id);
        Assert.Equal("All off", found.Name);
        Assert.Single(found.Targets);
        Assert.Equal(false, found.Targets[0].Set["on_off"]);
    }
}
