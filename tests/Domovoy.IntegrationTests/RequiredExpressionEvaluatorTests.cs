// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Automations;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the Epic 3E required-expression gate (Hubitat "Required Expression"): the boolean parser
/// over indexed conditions (<c>C0</c>, <c>C1</c>, …) combined with <c>&amp;&amp; || ! ( )</c>. Conditions are
/// device-state predicates evaluated against a seeded <see cref="DeviceRegistry"/> through the real
/// <see cref="RuleEvaluator"/>, so the whole "condition → boolean → expression" path is exercised.
/// </summary>
public sealed class RequiredExpressionEvaluatorTests
{
    private static readonly Guid DevA = Guid.NewGuid();
    private static readonly Guid DevB = Guid.NewGuid();
    private static readonly Guid DevC = Guid.NewGuid();

    // Registry seeded so: A.on_off=true, B.on_off=false, C.value=10.
    private static (RequiredExpressionEvaluator Eval, DeviceRegistry Registry) Build()
    {
        var registry = new DeviceRegistry();
        registry.SetValue(DevA, "on_off", true);
        registry.SetValue(DevB, "on_off", false);
        registry.SetValue(DevC, "value", 10d);
        var evaluator = new RuleEvaluator(registry, new SunCalculator(0, 0));
        return (new RequiredExpressionEvaluator(evaluator, NullLogger<RequiredExpressionEvaluator>.Instance), registry);
    }

    private static RuleCondition DeviceEq(Guid id, string cap, object? value, string op = "eq") => new()
    {
        Type = ConditionType.DeviceState, DeviceId = id.ToString(), CapabilityId = cap, Operator = op, Value = value,
    };

    private static bool Holds(string? expression, params RuleCondition[] conditions)
    {
        var (eval, _) = Build();
        var gate = new RequiredExpression { Expression = expression ?? "", Conditions = conditions.ToList() };
        return eval.Holds(gate, DateTimeOffset.UtcNow, mode: null);
    }

    [Fact]
    public void NullGate_AlwaysHolds()
    {
        var (eval, _) = Build();
        Assert.True(eval.Holds(null, DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void EmptyConditions_AlwaysHolds()
    {
        var (eval, _) = Build();
        Assert.True(eval.Holds(new RequiredExpression(), DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void BlankExpression_IsAndOfAllConditions()
    {
        // C0 (A on) true AND C1 (B on) false ⇒ false.
        Assert.False(Holds("", DeviceEq(DevA, "on_off", true), DeviceEq(DevB, "on_off", true)));
        // C0 true AND C1 (B off) true ⇒ true.
        Assert.True(Holds("", DeviceEq(DevA, "on_off", true), DeviceEq(DevB, "on_off", false)));
    }

    [Fact]
    public void SingleCondition_ReflectsTruth()
    {
        Assert.True(Holds("C0", DeviceEq(DevA, "on_off", true)));
        Assert.False(Holds("C0", DeviceEq(DevB, "on_off", true))); // B is off
    }

    [Fact]
    public void Negation_Inverts()
    {
        Assert.True(Holds("!C0", DeviceEq(DevB, "on_off", true)));  // B off ⇒ C0 false ⇒ !C0 true
        Assert.False(Holds("!C0", DeviceEq(DevA, "on_off", true))); // A on ⇒ C0 true ⇒ !C0 false
    }

    [Fact]
    public void OrShortCircuit_And_Precedence()
    {
        // C0 (true) || (C1 && C2): first disjunct true ⇒ whole true regardless of the rest.
        Assert.True(Holds("C0 || (C1 && C2)",
            DeviceEq(DevA, "on_off", true),   // C0 true
            DeviceEq(DevB, "on_off", true),   // C1 false
            DeviceEq(DevC, "value", 10)));    // C2 true
    }

    [Fact]
    public void Parentheses_ChangeGrouping()
    {
        // !(C0 && C1): C0 true, C1 (C.value gt 5) true ⇒ inner true ⇒ negated false.
        Assert.False(Holds("!(C0 && C1)",
            DeviceEq(DevA, "on_off", true),
            DeviceEq(DevC, "value", 5, "gt")));
        // !(C0 && C1) where C1 false ⇒ inner false ⇒ negated true.
        Assert.True(Holds("!(C0 && C1)",
            DeviceEq(DevA, "on_off", true),
            DeviceEq(DevC, "value", 50, "gt"))); // 10 > 50 is false
    }

    [Theory]
    [InlineData("C0 &&")]        // dangling operator
    [InlineData("C0 && (C1")]    // missing ')'
    [InlineData("C5")]           // index out of range (only C0..C1 exist)
    [InlineData("C0 AND C1")]    // unrecognized token
    public void MalformedExpression_FailsClosed(string expression)
    {
        // A malformed gate must never let a rule run ungated — it evaluates to false, not true.
        Assert.False(Holds(expression, DeviceEq(DevA, "on_off", true), DeviceEq(DevB, "on_off", false)));
    }
}
