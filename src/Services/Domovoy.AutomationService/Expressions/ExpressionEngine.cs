// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;

namespace Domovoy.AutomationService.Expressions;

/// <summary>
/// A deliberately tiny arithmetic expression language for the <c>expression</c> control block (roadmap Epic
/// 2Q, Phase 3). It is NOT a scripting runtime: there are no loops, no function definitions and no variables
/// that persist between runs — it can only compute outputs from inputs each tick, so it can't grow into an
/// interpreter. A program is a few lines of <c>OUTn = &lt;expr&gt;</c> (an output) or <c>[let] name = &lt;expr&gt;</c>
/// (a local); expressions support <c>+ - * / %</c>, comparisons, <c>&amp;&amp; || !</c>, a ternary, a whitelist
/// of math functions, and the built-ins <c>dt</c> (seconds since the last tick) and <c>prev(OUTn)</c> (last
/// emitted value) so a filter/hysteresis fits on one line. Parsed once (bounded by hard line/node caps) then
/// evaluated per tick; booleans are carried as 1/0.
/// </summary>
public sealed class ExpressionProgram
{
    private const int MaxLines = 16;
    private const int MaxScriptLength = 2000;
    private const int MaxNodes = 500;

    private readonly IReadOnlyList<Statement> _statements;

    /// <summary>Output names assigned by the program (e.g. <c>OUT1</c>), in first-seen order.</summary>
    public IReadOnlyList<string> Outputs { get; }

    private ExpressionProgram(IReadOnlyList<Statement> statements, IReadOnlyList<string> outputs)
    {
        _statements = statements;
        Outputs = outputs;
    }

    /// <summary>Parse a script. Throws <see cref="FormatException"/> on any syntax/limit violation.</summary>
    public static ExpressionProgram Parse(string script)
    {
        if (script is null) throw new FormatException("empty script");
        if (script.Length > MaxScriptLength) throw new FormatException($"script exceeds {MaxScriptLength} chars");

        var lines = script.Replace("\r", "").Split('\n');
        var statements = new List<Statement>();
        var outputs = new List<string>();
        var nodes = 0;

        foreach (var raw in lines)
        {
            var hash = raw.IndexOf('#');
            var line = (hash >= 0 ? raw[..hash] : raw).Trim();
            if (line.Length == 0) continue;
            if (statements.Count >= MaxLines) throw new FormatException($"more than {MaxLines} statements");

            var parser = new Parser(line, () => { if (++nodes > MaxNodes) throw new FormatException("expression too large"); });
            var stmt = parser.ParseStatement();
            if (stmt.IsOutput && !outputs.Contains(stmt.Target, StringComparer.OrdinalIgnoreCase))
                outputs.Add(stmt.Target);
            statements.Add(stmt);
        }

        if (statements.Count == 0) throw new FormatException("no statements");
        if (outputs.Count == 0) throw new FormatException("no OUT assignments");
        return new ExpressionProgram(statements, outputs);
    }

    /// <summary>
    /// Evaluate the program against <paramref name="env"/>. Returns output name → value. Returns an empty map
    /// when a referenced input is unbound (the block then holds/emits nothing) so a program waits for its data.
    /// </summary>
    public Dictionary<string, double> Run(IExprEnv env)
    {
        var scope = new Scope(env);
        var outputs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var stmt in _statements)
            {
                var value = stmt.Expr.Eval(scope);
                if (stmt.IsOutput) outputs[stmt.Target] = value;
                else scope.Locals[stmt.Target] = value;
            }
        }
        catch (MissingInputException)
        {
            return new Dictionary<string, double>();
        }
        return outputs;
    }

    // ── AST ────────────────────────────────────────────────────────────────────────────────────

    private sealed record Statement(string Target, bool IsOutput, Node Expr);

    private sealed class Scope
    {
        public Scope(IExprEnv env) => Env = env;
        public IExprEnv Env { get; }
        public Dictionary<string, double> Locals { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private abstract class Node { public abstract double Eval(Scope s); }

    private sealed class NumNode : Node
    {
        private readonly double _v;
        public NumNode(double v) => _v = v;
        public override double Eval(Scope s) => _v;
    }

    private sealed class VarNode : Node
    {
        private readonly string _name;
        public VarNode(string name) => _name = name;
        public override double Eval(Scope s)
        {
            if (_name.Equals("dt", StringComparison.OrdinalIgnoreCase)) return s.Env.Dt;
            if (s.Locals.TryGetValue(_name, out var local)) return local;
            if (_name.StartsWith("IN", StringComparison.OrdinalIgnoreCase))
                return s.Env.Input(_name) ?? throw new MissingInputException();
            throw new FormatException($"unknown identifier '{_name}'");
        }
    }

    private sealed class UnaryNode : Node
    {
        private readonly char _op; private readonly Node _c;
        public UnaryNode(char op, Node c) { _op = op; _c = c; }
        public override double Eval(Scope s)
        {
            var v = _c.Eval(s);
            return _op == '!' ? (v == 0 ? 1 : 0) : -v;
        }
    }

    private sealed class BinaryNode : Node
    {
        private readonly string _op; private readonly Node _l, _r;
        public BinaryNode(string op, Node l, Node r) { _op = op; _l = l; _r = r; }
        public override double Eval(Scope s)
        {
            // Short-circuit for boolean ops.
            if (_op == "&&") return (_l.Eval(s) != 0 && _r.Eval(s) != 0) ? 1 : 0;
            if (_op == "||") return (_l.Eval(s) != 0 || _r.Eval(s) != 0) ? 1 : 0;
            double a = _l.Eval(s), b = _r.Eval(s);
            return _op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => a / b,
                "%" => a % b,
                "<" => a < b ? 1 : 0,
                "<=" => a <= b ? 1 : 0,
                ">" => a > b ? 1 : 0,
                ">=" => a >= b ? 1 : 0,
                "==" => a == b ? 1 : 0,
                "!=" => a != b ? 1 : 0,
                _ => throw new FormatException($"bad operator '{_op}'"),
            };
        }
    }

    private sealed class TernaryNode : Node
    {
        private readonly Node _c, _a, _b;
        public TernaryNode(Node c, Node a, Node b) { _c = c; _a = a; _b = b; }
        public override double Eval(Scope s) => _c.Eval(s) != 0 ? _a.Eval(s) : _b.Eval(s);
    }

    private sealed class CallNode : Node
    {
        private readonly string _name; private readonly List<Node> _args;
        public CallNode(string name, List<Node> args) { _name = name; _args = args; }

        public override double Eval(Scope s)
        {
            if (_name.Equals("prev", StringComparison.OrdinalIgnoreCase))
            {
                if (_args.Count != 1 || _args[0] is not VarRef vr)
                    throw new FormatException("prev(OUTn) takes one output name");
                return s.Env.Prev(vr.Name);
            }

            var a = _args.Select(n => n.Eval(s)).ToArray();
            double Need(int n) => a.Length == n ? 0 : throw new FormatException($"{_name}() expects {n} args");
            return _name.ToLowerInvariant() switch
            {
                "abs" => Need(1) + Math.Abs(a[0]),
                "sqrt" => Need(1) + Math.Sqrt(a[0]),
                "exp" => Need(1) + Math.Exp(a[0]),
                "floor" => Need(1) + Math.Floor(a[0]),
                "ceil" => Need(1) + Math.Ceiling(a[0]),
                "round" => Need(1) + Math.Round(a[0]),
                "pow" => Need(2) + Math.Pow(a[0], a[1]),
                "clamp" => Need(3) + Math.Clamp(a[0], a[1], a[2]),
                "min" => a.Length >= 1 ? a.Min() : throw new FormatException("min() needs args"),
                "max" => a.Length >= 1 ? a.Max() : throw new FormatException("max() needs args"),
                "avg" => a.Length >= 1 ? a.Average() : throw new FormatException("avg() needs args"),
                _ => throw new FormatException($"unknown function '{_name}'"),
            };
        }
    }

    /// <summary>A bare identifier captured as an argument (only meaningful to <c>prev(OUTn)</c>).</summary>
    private sealed class VarRef : Node
    {
        public string Name { get; }
        private readonly VarNode _inner;
        public VarRef(string name) { Name = name; _inner = new VarNode(name); }
        public override double Eval(Scope s) => _inner.Eval(s);
    }

    private sealed class MissingInputException : Exception { }

    // ── Parser (recursive descent, precedence climbing) ──────────────────────────────────────────

    private sealed class Parser
    {
        private readonly List<Tok> _toks;
        private readonly Action _count;
        private int _i;

        public Parser(string line, Action countNode)
        {
            _toks = Lex(line);
            _count = countNode;
        }

        public Statement ParseStatement()
        {
            // optional 'let'
            if (Peek().Kind == TokKind.Ident && Peek().Text.Equals("let", StringComparison.OrdinalIgnoreCase)) _i++;

            var target = Expect(TokKind.Ident).Text;
            ExpectOp("=");
            var expr = ParseTernary();
            if (Peek().Kind != TokKind.End) throw new FormatException("unexpected trailing tokens");

            var isOutput = target.StartsWith("OUT", StringComparison.OrdinalIgnoreCase)
                && target.Length > 3 && char.IsDigit(target[3]);
            return new Statement(isOutput ? target.ToUpperInvariant() : target, isOutput, expr);
        }

        private Node ParseTernary()
        {
            var cond = ParseOr();
            if (IsOp("?"))
            {
                _i++;
                var a = ParseTernary();
                ExpectOp(":");
                var b = ParseTernary();
                return N(new TernaryNode(cond, a, b));
            }
            return cond;
        }

        private Node ParseOr()
        {
            var n = ParseAnd();
            while (IsOp("||")) { _i++; n = N(new BinaryNode("||", n, ParseAnd())); }
            return n;
        }

        private Node ParseAnd()
        {
            var n = ParseEquality();
            while (IsOp("&&")) { _i++; n = N(new BinaryNode("&&", n, ParseEquality())); }
            return n;
        }

        private Node ParseEquality()
        {
            var n = ParseComparison();
            while (IsOp("==") || IsOp("!=")) { var op = Next().Text; n = N(new BinaryNode(op, n, ParseComparison())); }
            return n;
        }

        private Node ParseComparison()
        {
            var n = ParseAdditive();
            while (IsOp("<") || IsOp("<=") || IsOp(">") || IsOp(">="))
            { var op = Next().Text; n = N(new BinaryNode(op, n, ParseAdditive())); }
            return n;
        }

        private Node ParseAdditive()
        {
            var n = ParseMultiplicative();
            while (IsOp("+") || IsOp("-")) { var op = Next().Text; n = N(new BinaryNode(op, n, ParseMultiplicative())); }
            return n;
        }

        private Node ParseMultiplicative()
        {
            var n = ParseUnary();
            while (IsOp("*") || IsOp("/") || IsOp("%")) { var op = Next().Text; n = N(new BinaryNode(op, n, ParseUnary())); }
            return n;
        }

        private Node ParseUnary()
        {
            if (IsOp("-")) { _i++; return N(new UnaryNode('-', ParseUnary())); }
            if (IsOp("!")) { _i++; return N(new UnaryNode('!', ParseUnary())); }
            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            var t = Peek();
            if (t.Kind == TokKind.Number) { _i++; return N(new NumNode(t.Num)); }
            if (IsOp("(")) { _i++; var e = ParseTernary(); ExpectOp(")"); return e; }
            if (t.Kind == TokKind.Ident)
            {
                _i++;
                if (t.Text.Equals("true", StringComparison.OrdinalIgnoreCase)) return N(new NumNode(1));
                if (t.Text.Equals("false", StringComparison.OrdinalIgnoreCase)) return N(new NumNode(0));
                if (IsOp("(")) // function call
                {
                    _i++;
                    var args = new List<Node>();
                    if (!IsOp(")"))
                    {
                        args.Add(ArgNode());
                        while (IsOp(",")) { _i++; args.Add(ArgNode()); }
                    }
                    ExpectOp(")");
                    return N(new CallNode(t.Text, args));
                }
                return N(new VarNode(t.Text));
            }
            throw new FormatException($"unexpected token '{t.Text}'");
        }

        // An argument that is a bare identifier is kept as a VarRef (so prev(OUTn) can read the name).
        private Node ArgNode()
        {
            if (Peek().Kind == TokKind.Ident && (_i + 1 >= _toks.Count || !(_toks[_i + 1] is { Kind: TokKind.Op } o && o.Text == "(")))
            {
                var ahead = _toks[_i];
                // only treat as VarRef when it's a standalone identifier argument
                if (_i + 1 < _toks.Count && _toks[_i + 1] is { Kind: TokKind.Op } nxt && (nxt.Text == "," || nxt.Text == ")"))
                {
                    _i++;
                    return N(new VarRef(ahead.Text));
                }
            }
            return ParseTernary();
        }

        private Node N(Node n) { _count(); return n; }

        private Tok Peek() => _i < _toks.Count ? _toks[_i] : new Tok(TokKind.End, "", 0);
        private Tok Next() => _toks[_i++];
        private bool IsOp(string op) => Peek() is { Kind: TokKind.Op } t && t.Text == op;
        private Tok Expect(TokKind k) => Peek().Kind == k ? Next() : throw new FormatException($"expected {k}");
        private void ExpectOp(string op) { if (!IsOp(op)) throw new FormatException($"expected '{op}'"); _i++; }

        private static List<Tok> Lex(string s)
        {
            var toks = new List<Tok>();
            int i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    int start = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                    toks.Add(new Tok(TokKind.Number, s[start..i], double.Parse(s[start..i], CultureInfo.InvariantCulture)));
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    toks.Add(new Tok(TokKind.Ident, s[start..i], 0));
                    continue;
                }
                // two-char operators first
                if (i + 1 < s.Length)
                {
                    var two = s.Substring(i, 2);
                    if (two is "<=" or ">=" or "==" or "!=" or "&&" or "||")
                    { toks.Add(new Tok(TokKind.Op, two, 0)); i += 2; continue; }
                }
                if ("+-*/%()<>=!?:,".IndexOf(c) >= 0) { toks.Add(new Tok(TokKind.Op, c.ToString(), 0)); i++; continue; }
                throw new FormatException($"unexpected character '{c}'");
            }
            return toks;
        }
    }

    private enum TokKind { Number, Ident, Op, End }
    private sealed record Tok(TokKind Kind, string Text, double Num);
}

/// <summary>The values an <see cref="ExpressionProgram"/> reads while evaluating (supplied by the block).</summary>
public interface IExprEnv
{
    /// <summary>Bound input value by name (e.g. <c>IN1</c>), or null when unbound.</summary>
    double? Input(string name);

    /// <summary>Last emitted value of an output (for <c>prev(OUTn)</c>); 0 if none yet.</summary>
    double Prev(string outputName);

    /// <summary>Seconds since the previous tick (0 on the first).</summary>
    double Dt { get; }
}
