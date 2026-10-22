// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.Contracts.Automations;
using Domovoy.DbGateway.Endpoints;

using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Persistence round-trip against real Mongo (roadmap Epic 3E global variables): a <see cref="GlobalVariable"/>
/// whose value arrives as a <see cref="JsonElement"/> (as ASP.NET model binding produces) must flatten to a
/// BCL primitive — via the same <see cref="AutomationEndpoints.ToPlain"/> the endpoint uses — before Mongo's
/// <c>ObjectSerializer</c> will store it, and a value-only update must not disturb name/type/description.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")]
public sealed class VariablePersistenceTests
{
    private readonly InfraFixture _fx;
    public VariablePersistenceTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<GlobalVariable> Variables =>
        _fx.Db.GetCollection<GlobalVariable>(VariableEndpoints.Collection);

    private static object? Json(string literal) => JsonSerializer.Deserialize<object>(literal);

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task Variable_WithJsonElementValue_RoundTripsThroughMongo()
    {
        var variable = new GlobalVariable
        {
            Id = Guid.NewGuid().ToString(),
            Name = "boost_target",
            Type = VariableType.Number,
            Value = AutomationEndpoints.ToPlain(Json("21.5")), // as the POST endpoint normalizes it
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await Variables.InsertOneAsync(variable); // would throw without ToPlain flattening

        var stored = await Variables.Find(x => x.Id == variable.Id).FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal("boost_target", stored!.Name);
        Assert.Equal(VariableType.Number, stored.Type);
        Assert.Equal(21.5, Convert.ToDouble(stored.Value));
    }

    [Fact]
    public async Task ValueOnlyUpdate_PreservesNameTypeDescription()
    {
        var id = Guid.NewGuid().ToString();
        await Variables.InsertOneAsync(new GlobalVariable
        {
            Id = id, Name = "away_counter", Type = VariableType.Number,
            Description = "times the house went Away today", Value = 0d,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });

        // Mirror the /{id}/value endpoint: set only Value + UpdatedAt.
        var update = Builders<GlobalVariable>.Update
            .Set(x => x.Value, AutomationEndpoints.ToPlain(Json("3")))
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        var result = await Variables.UpdateOneAsync(x => x.Id == id, update);
        Assert.Equal(1, result.MatchedCount);

        var stored = await Variables.Find(x => x.Id == id).FirstOrDefaultAsync();
        Assert.Equal(3L, Convert.ToInt64(stored!.Value)); // value updated
        Assert.Equal("away_counter", stored.Name);        // config untouched
        Assert.Equal("times the house went Away today", stored.Description);
        Assert.Equal(VariableType.Number, stored.Type);
    }

    [Fact]
    public async Task BooleanVariable_RoundTrips()
    {
        var id = Guid.NewGuid().ToString();
        await Variables.InsertOneAsync(new GlobalVariable
        {
            Id = id, Name = "guest_mode", Type = VariableType.Boolean,
            Value = AutomationEndpoints.ToPlain(Json("true")),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });

        var stored = await Variables.Find(x => x.Id == id).FirstOrDefaultAsync();
        Assert.Equal(true, stored!.Value);
        Assert.Equal(VariableType.Boolean, stored.Type);
    }
}
