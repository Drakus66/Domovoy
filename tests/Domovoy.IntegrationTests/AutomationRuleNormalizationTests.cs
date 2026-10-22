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
/// Regression for a live bug (found while seeding demo proposals, Epic 2C): a rule POSTed over HTTP
/// carries its trigger/condition/action values as <see cref="JsonElement"/>, which the Mongo
/// <c>ObjectSerializer</c> refuses to persist — every API-created rule with a comparison value failed
/// to save. <see cref="AutomationEndpoints.NormalizeJsonValues"/> must flatten them to BCL primitives
/// that round-trip through a real Mongo.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")] // needs Docker (Testcontainers); excluded from the unit-only CI job
public sealed class AutomationRuleNormalizationTests
{
    private readonly InfraFixture _fx;
    public AutomationRuleNormalizationTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<AutomationRule> Rules =>
        _fx.Db.GetCollection<AutomationRule>(AutomationEndpoints.Collection);

    private static object? Json(string literal) => JsonSerializer.Deserialize<object>(literal);

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task HttpBoundRule_WithJsonElementValues_RoundTripsThroughMongo()
    {
        // Values exactly as ASP.NET model binding produces them: JsonElement, not primitives.
        var rule = new AutomationRule
        {
            Id = Guid.NewGuid().ToString(),
            Name = "api-created rule",
            Status = RuleStatus.Proposed,
            Triggers =
            {
                new RuleTrigger
                {
                    Type = TriggerType.DeviceState, DeviceId = "dev-motion",
                    CapabilityId = "motion", Operator = "eq", Value = Json("true"),
                },
            },
            Conditions =
            {
                new RuleCondition
                {
                    Type = ConditionType.DeviceState, DeviceId = "dev-lux",
                    CapabilityId = "illuminance", Operator = "lt", Value = Json("40.5"),
                },
            },
            Actions =
            {
                new RuleAction
                {
                    Type = ActionType.Command, DeviceId = "dev-lamp",
                    Set = new Dictionary<string, object?> { ["on_off"] = Json("true"), ["brightness"] = Json("80") },
                },
            },
        };

        AutomationEndpoints.NormalizeJsonValues(rule);
        await Rules.InsertOneAsync(rule); // would throw BsonSerializationException without normalization

        var stored = await Rules.Find(x => x.Id == rule.Id).FirstOrDefaultAsync();
        Assert.Equal(true, stored.Triggers[0].Value);
        Assert.Equal(40.5, Assert.IsType<double>(stored.Conditions[0].Value));
        Assert.Equal(true, stored.Actions[0].Set!["on_off"]);
        Assert.Equal(80d, Convert.ToDouble(stored.Actions[0].Set!["brightness"]));
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task Rule_WithEpic3EFields_NormalizesNestedJsonElements()
    {
        // Every Epic 3E surface that carries an object? value must be flattened too, or the whole rule fails
        // to persist: the required-expression gate's conditions, a WaitForEvent's WaitValue, and the values
        // inside its OnTimeout / OnError branch actions.
        var rule = new AutomationRule
        {
            Id = Guid.NewGuid().ToString(),
            Name = "3e rule",
            Status = RuleStatus.Active,
            Triggers = { new RuleTrigger { Type = TriggerType.DeviceState, DeviceId = "d", CapabilityId = "contact", Operator = "eq", Value = Json("true") } },
            RequiredExpression = new RequiredExpression
            {
                Expression = "C0",
                Conditions = { new RuleCondition { Type = ConditionType.DeviceState, DeviceId = "d2", CapabilityId = "occupancy", Operator = "eq", Value = Json("true") } },
            },
            Actions =
            {
                new RuleAction
                {
                    Type = ActionType.WaitForEvent,
                    WaitDeviceId = "d3", WaitCapabilityId = "contact", WaitOperator = "eq", WaitValue = Json("true"),
                    TimeoutSeconds = 60,
                    OnTimeout = new()
                    {
                        new RuleAction { Type = ActionType.Command, DeviceId = "d4", Set = new() { ["on_off"] = Json("false") } },
                    },
                    OnError = new()
                    {
                        new RuleAction { Type = ActionType.Command, DeviceId = "d5", Set = new() { ["brightness"] = Json("10") } },
                    },
                },
            },
        };

        AutomationEndpoints.NormalizeJsonValues(rule);
        await Rules.InsertOneAsync(rule); // BsonSerializationException if any nested JsonElement survived

        var stored = await Rules.Find(x => x.Id == rule.Id).FirstOrDefaultAsync();
        Assert.Equal(true, stored.RequiredExpression!.Conditions[0].Value);
        Assert.Equal(true, stored.Actions[0].WaitValue);
        Assert.Equal(false, stored.Actions[0].OnTimeout![0].Set!["on_off"]);
        Assert.Equal(10d, Convert.ToDouble(stored.Actions[0].OnError![0].Set!["brightness"]));
    }
}
