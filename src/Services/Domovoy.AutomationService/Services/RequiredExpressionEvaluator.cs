// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.RegularExpressions;

using Domovoy.Contracts.Automations;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Evaluates a rule's live "required expression" gate (roadmap Epic 3E, Hubitat "Required Expression"):
/// a boolean combination (<c>&amp;&amp; || ! ( )</c>) over the SAME predicate shape as <see cref="RuleCondition"/>
/// — reusing <see cref="RuleEvaluator.ConditionHolds"/> — referenced by index as <c>C0</c>, <c>C1</c>, ….
/// Deliberately not the 2Q <c>ExpressionEngine</c>: that engine's identifiers are hardcoded to control-block
/// <c>IN1..IN4</c> ports and it has no notion of a condition/predicate value, so reusing it here would mean
/// bolting a second, unrelated binding scheme onto it rather than an extension.
/// </summary>
public sealed class RequiredExpressionEvaluator
{
    private readonly RuleEvaluator _evaluator;
    private readonly ILogger<RequiredExpressionEvaluator> _logger;

    public RequiredExpressionEvaluator(RuleEvaluator evaluator, ILogger<RequiredExpressionEvaluator> logger)
    {
        _evaluator = evaluator;
        _logger = logger;
    }

    /// <summary>True when there is no gate, or the gate's expression currently evaluates true. A malformed
    /// expression (bad syntax, out-of-range <c>Cn</c>) fails CLOSED (returns false) rather than silently
    /// letting the rule run ungated.</summary>
    public bool Holds(RequiredExpression? gate, DateTimeOffset now, string? mode)
    {
        if (gate is null || gate.Conditions.Count == 0) return true;

        var values = gate.Conditions.Select(c => _evaluator.ConditionHolds(c, now, mode)).ToArray();
        var expression = string.IsNullOrWhiteSpace(gate.Expression)
            ? string.Join(" && ", Enumerable.Range(0, values.Length).Select(i => $"C{i}")) // blank ⇒ AND of all
            : gate.Expression;

        try
        {
            return new Parser(expression, values).ParseFully();
        }
        catch (FormatException ex)
        {
            _logger.LogWarning("Malformed required expression '{Expression}': {Message} — gate closed", expression, ex.Message);
            return false;
        }
    }

    private static readonly Regex Tokenizer = new(@"\(|\)|&&|\|\||!|C\d+", RegexOptions.Compiled);

    /// <summary>Tiny recursive-descent boolean parser: expr := or ; or := and ('||' and)* ;
    /// and := not ('&amp;&amp;' not)* ; not := '!' not | atom ; atom := 'C'digits | '(' expr ')'.</summary>
    private sealed class Parser
    {
        private readonly IReadOnlyList<string> _tokens;
        private readonly bool[] _values;
        private int _pos;

        public Parser(string expression, bool[] values)
        {
            var matches = Tokenizer.Matches(expression);
            // Reject stray characters (e.g. typos) instead of silently ignoring them.
            var consumed = matches.Sum(m => m.Length);
            var stripped = Regex.Replace(expression, @"\s+", "");
            if (consumed != stripped.Length)
                throw new FormatException("unrecognized token in expression");

            _tokens = matches.Select(m => m.Value).ToList();
            _values = values;
        }

        public bool ParseFully()
        {
            if (_tokens.Count == 0) throw new FormatException("empty expression");
            var result = ParseOr();
            if (_pos != _tokens.Count) throw new FormatException($"unexpected token '{_tokens[_pos]}'");
            return result;
        }

        private bool ParseOr()
        {
            var value = ParseAnd();
            while (Peek() == "||") { _pos++; value = ParseAnd() || value; }
            return value;
        }

        private bool ParseAnd()
        {
            var value = ParseNot();
            while (Peek() == "&&") { _pos++; value = ParseNot() && value; }
            return value;
        }

        private bool ParseNot()
        {
            if (Peek() == "!") { _pos++; return !ParseNot(); }
            return ParseAtom();
        }

        private bool ParseAtom()
        {
            var token = Peek() ?? throw new FormatException("unexpected end of expression");
            if (token == "(")
            {
                _pos++;
                var value = ParseOr();
                if (Peek() != ")") throw new FormatException("missing ')'");
                _pos++;
                return value;
            }
            if (token[0] == 'C' && int.TryParse(token.AsSpan(1), out var index))
            {
                _pos++;
                if (index < 0 || index >= _values.Length)
                    throw new FormatException($"'{token}' out of range (only {_values.Length} condition(s))");
                return _values[index];
            }
            throw new FormatException($"unexpected token '{token}'");
        }

        private string? Peek() => _pos < _tokens.Count ? _tokens[_pos] : null;
    }
}
