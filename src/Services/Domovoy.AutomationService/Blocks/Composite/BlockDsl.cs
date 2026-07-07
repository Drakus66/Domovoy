// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;
using System.Text;

using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Blocks.Composite;

/// <summary>
/// The canonical text DSL for composite blocks (roadmap Epic 1H, authoring track B). A pipeline reads
/// left-to-right, each <c>|&gt;</c> feeding the previous stage's primary output into the next stage's primary
/// input — expressing filter → controller composition as one diff-able line:
/// <code>input(temperature) |&gt; ewma(tau=300) |&gt; thermostat(setpoint=21, hysteresis=0.5)</code>
/// It parses to a <see cref="CompositeDefinition"/> (and serializes back), so a composite is authored as data —
/// no code, no rebuild.
/// <para>
/// Two authoring forms, one document model (roadmap Epic 1H E2). The <b>pipe</b> form above is the ~80% linear
/// shorthand. The <b>graph</b> form expresses branching — fan-out (one source into several nodes), fan-in (a node
/// wired from several sources) and multiple outputs — as labelled statements:
/// <code>
/// in temperature
/// in co2
/// heat = thermostat(setpoint=21) &lt;- temperature
/// vent = co2_ventilation(threshold=800) &lt;- co2
/// out heat_demand = heat.on_off
/// out vent_demand = vent.on_off
/// </code>
/// <see cref="Parse"/> auto-detects: a document containing <c>|&gt;</c> is a pipe, otherwise it is a graph.
/// </para>
/// </summary>
public static class BlockDsl
{
    /// <summary>
    /// Parse a composite document (pipe or graph form — auto-detected) into a definition. <paramref name="resolveType"/>
    /// supplies the referenced types (for port/output discovery). Throws <see cref="UnknownBlockTypeException"/> when a
    /// referenced type is unresolvable, or <see cref="FormatException"/> on otherwise-malformed input.
    /// </summary>
    public static CompositeDefinition Parse(string typeId, string title, string description, string dsl, Func<string, IBlockType?> resolveType)
        => dsl.Contains("|>", StringComparison.Ordinal)
            ? ParsePipe(typeId, title, description, dsl, resolveType)
            : ParseGraph(typeId, title, description, dsl, resolveType);

    // Linear pipe form: input(x) |> a(..) |> b(..) — threads each stage's primary output into the next's primary input.
    private static CompositeDefinition ParsePipe(string typeId, string title, string description, string dsl, Func<string, IBlockType?> resolveType)
    {
        var stages = dsl.Split("|>", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (stages.Length < 2) throw new FormatException("a composite needs an input() and at least one stage");

        var (head, inputArg) = ParseCall(stages[0]);
        if (!string.Equals(head, "input", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("a pipeline must start with input(<port>)");
        var inputName = inputArg.Keys.FirstOrDefault() ?? throw new FormatException("input() needs a port name, e.g. input(temperature)");

        var nodes = new List<CompositeNode>();
        CompositeInput? compositeInput = null;
        string prevSource = $"${inputName}"; // the first stage reads from the composite input

        for (var i = 1; i < stages.Length; i++)
        {
            var (nodeType, args) = ParseCall(stages[i]);
            var type = resolveType(nodeType) ?? throw new UnknownBlockTypeException(nodeType);
            if (type.Inputs.Count == 0) throw new FormatException($"'{nodeType}' has no input to pipe into");

            var nodeId = $"n{i - 1}";
            var primaryInput = type.Inputs[0];
            nodes.Add(new CompositeNode(nodeId, type.TypeId, args,
                new Dictionary<string, string> { [primaryInput.Name] = prevSource }));

            compositeInput ??= new CompositeInput(inputName, primaryInput.Kind, $"Input for {typeId}");

            var primaryOutput = type.Outputs.FirstOrDefault()
                ?? throw new FormatException($"'{nodeType}' has no output to pipe onward");
            prevSource = $"{nodeId}#{primaryOutput.Id}";
        }

        // The composite output is the last stage's primary output.
        var lastNode = nodes[^1];
        var lastType = resolveType(lastNode.TypeId)!;
        var outCap = lastType.Outputs[0];
        var outputs = new List<CompositeOutput> { new(outCap, $"{lastNode.Id}#{outCap.Id}") };

        return new CompositeDefinition(typeId, title, description, new[] { compositeInput! }, outputs, nodes);
    }

    /// <summary>Serialize a linear composite back to canonical DSL (round-trips <see cref="Parse"/> output).</summary>
    public static string Serialize(CompositeDefinition def)
    {
        var sb = new StringBuilder();
        sb.Append("input(").Append(def.Inputs[0].Name).Append(')');
        foreach (var node in def.Nodes)
        {
            sb.Append(" |> ").Append(node.TypeId).Append('(');
            sb.Append(string.Join(", ", node.Params.Select(p => $"{p.Key}={p.Value.ToString(CultureInfo.InvariantCulture)}")));
            sb.Append(')');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parse the graph form (roadmap Epic 1H E2 branching). Statements are newline- or <c>;</c>-separated;
    /// <c>#</c> starts a comment. Three statement kinds:
    /// <list type="bullet">
    /// <item><c>in &lt;name&gt;</c> — declare a composite input (kind inferred from the first node port it feeds).</item>
    /// <item><c>&lt;label&gt; = &lt;type&gt;(&lt;params&gt;) [&lt;- src, src, …]</c> — a node; sources bind to the type's
    ///   input ports in order. A source is a declared input name or <c>&lt;label&gt;.&lt;capability&gt;</c>.</item>
    /// <item><c>out &lt;capabilityId&gt; = &lt;label&gt;.&lt;capability&gt;</c> — expose a node output as a composite
    ///   output (renaming lets two nodes with the same output id both surface, e.g. per-zone valves).</item>
    /// </list>
    /// </summary>
    private static CompositeDefinition ParseGraph(string typeId, string title, string description, string dsl, Func<string, IBlockType?> resolveType)
    {
        var statements = dsl
            .Split('\n', ';')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && !s.StartsWith('#'))
            .ToList();

        var inputNames = new List<string>();
        var nodes = new List<CompositeNode>();
        var nodeTypes = new Dictionary<string, IBlockType>(StringComparer.OrdinalIgnoreCase);
        var outputStmts = new List<(string OutId, string Label, string Cap)>();

        foreach (var stmt in statements)
        {
            if (stmt.StartsWith("in ", StringComparison.OrdinalIgnoreCase))
            {
                var name = stmt[3..].Trim();
                if (name.Length == 0) throw new FormatException($"'in' needs a port name in '{stmt}'");
                inputNames.Add(name);
            }
            else if (stmt.StartsWith("out ", StringComparison.OrdinalIgnoreCase))
            {
                var body = stmt[4..].Trim();
                var eq = body.IndexOf('=');
                if (eq < 0) throw new FormatException($"'out' needs '= <node>.<capability>' in '{stmt}'");
                var outId = body[..eq].Trim();
                var srcRef = body[(eq + 1)..].Trim();
                var dot = srcRef.IndexOf('.');
                if (dot < 0 || outId.Length == 0) throw new FormatException($"output must be 'out <id> = <node>.<capability>' in '{stmt}'");
                outputStmts.Add((outId, srcRef[..dot].Trim(), srcRef[(dot + 1)..].Trim()));
            }
            else
            {
                var eq = stmt.IndexOf('=');
                if (eq < 0) throw new FormatException($"unrecognized statement '{stmt}' — expected 'in', 'out', or '<label> = <type>(...)'");
                var label = stmt[..eq].Trim();
                var rhs = stmt[(eq + 1)..].Trim();

                string call;
                string[] srcs;
                var arrow = rhs.IndexOf("<-", StringComparison.Ordinal);
                if (arrow >= 0)
                {
                    call = rhs[..arrow].Trim();
                    srcs = rhs[(arrow + 2)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
                else { call = rhs; srcs = Array.Empty<string>(); }

                var (typeName, args) = ParseCall(call);
                var type = resolveType(typeName) ?? throw new UnknownBlockTypeException(typeName);
                if (label.Length == 0) throw new FormatException($"node needs a label in '{stmt}'");
                if (nodeTypes.ContainsKey(label)) throw new FormatException($"duplicate node label '{label}'");
                if (srcs.Length > type.Inputs.Count)
                    throw new FormatException($"'{typeName}' has {type.Inputs.Count} input(s) but {srcs.Length} wired in '{stmt}'");

                var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < srcs.Length; i++)
                    inputs[type.Inputs[i].Name] = ToWiredSource(srcs[i]);

                nodes.Add(new CompositeNode(label, type.TypeId, args, inputs));
                nodeTypes[label] = type;
            }
        }

        if (nodes.Count == 0) throw new FormatException("a composite needs at least one node");
        if (outputStmts.Count == 0) throw new FormatException("a composite needs at least one 'out' statement");

        // Validate every wired source references a declared input or a real node output.
        foreach (var node in nodes)
            foreach (var wired in node.Inputs.Values)
                ValidateSource(wired, inputNames, nodeTypes, node.Id);

        // Composite inputs — infer each port's kind from the first node port that consumes it (default Number).
        var inputs2 = inputNames
            .Select(name => new CompositeInput(name, InferInputKind(name, nodes, nodeTypes) ?? CapabilityKind.Number, $"Input {name} for {typeId}"))
            .ToList();

        // Composite outputs — rebuild each from the source node's output capability, renamed to the exposed id.
        var outputs = new List<CompositeOutput>();
        foreach (var (outId, label, cap) in outputStmts)
        {
            if (!nodeTypes.TryGetValue(label, out var t)) throw new FormatException($"output references unknown node '{label}'");
            var srcCap = t.Outputs.FirstOrDefault(c => string.Equals(c.Id, cap, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormatException($"node '{label}' has no output '{cap}'");
            var outCap = string.Equals(srcCap.Id, outId, StringComparison.Ordinal) ? srcCap : srcCap with { Id = outId };
            outputs.Add(new CompositeOutput(outCap, $"{label}#{cap}"));
        }

        return new CompositeDefinition(typeId, title, description, inputs2, outputs, nodes);
    }

    // A DSL source token → wiring form: "<label>.<cap>" → "<label>#<cap>"; a bare name → "$name" (a composite input).
    private static string ToWiredSource(string src)
    {
        var dot = src.IndexOf('.');
        return dot >= 0 ? $"{src[..dot].Trim()}#{src[(dot + 1)..].Trim()}" : $"${src}";
    }

    private static void ValidateSource(string wired, List<string> inputNames, Dictionary<string, IBlockType> nodeTypes, string ownerLabel)
    {
        if (wired.StartsWith('$'))
        {
            if (!inputNames.Contains(wired[1..], StringComparer.OrdinalIgnoreCase))
                throw new FormatException($"node '{ownerLabel}' wires undeclared input '{wired[1..]}'");
            return;
        }
        var hash = wired.IndexOf('#');
        var label = wired[..hash];
        var cap = wired[(hash + 1)..];
        if (!nodeTypes.TryGetValue(label, out var t))
            throw new FormatException($"node '{ownerLabel}' wires unknown node '{label}'");
        if (!t.Outputs.Any(c => string.Equals(c.Id, cap, StringComparison.OrdinalIgnoreCase)))
            throw new FormatException($"node '{ownerLabel}' wires '{label}.{cap}' but '{label}' has no such output");
    }

    private static CapabilityKind? InferInputKind(string name, List<CompositeNode> nodes, Dictionary<string, IBlockType> nodeTypes)
    {
        foreach (var node in nodes)
            foreach (var (port, wired) in node.Inputs)
                if (wired == $"${name}")
                    return nodeTypes[node.Id].Inputs.FirstOrDefault(p => string.Equals(p.Name, port, StringComparison.OrdinalIgnoreCase))?.Kind;
        return null;
    }

    /// <summary>Serialize a composite to the graph DSL (round-trips <see cref="ParseGraph"/>; the general form).</summary>
    public static string SerializeGraph(CompositeDefinition def)
    {
        var sb = new StringBuilder();
        foreach (var input in def.Inputs) sb.Append("in ").Append(input.Name).Append('\n');
        foreach (var node in def.Nodes)
        {
            sb.Append(node.Id).Append(" = ").Append(node.TypeId).Append('(');
            sb.Append(string.Join(", ", node.Params.Select(p => $"{p.Key}={p.Value.ToString(CultureInfo.InvariantCulture)}")));
            sb.Append(')');
            if (node.Inputs.Count > 0)
                sb.Append(" <- ").Append(string.Join(", ", node.Inputs.Values.Select(FromWiredSource)));
            sb.Append('\n');
        }
        foreach (var o in def.Outputs)
            sb.Append("out ").Append(o.Capability.Id).Append(" = ").Append(FromWiredSource(o.Source)).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    // Wiring form → DSL source token (inverse of ToWiredSource): "$name" → "name"; "label#cap" → "label.cap".
    private static string FromWiredSource(string wired) =>
        wired.StartsWith('$') ? wired[1..] : wired.Replace('#', '.');

    // Parse "name(k=v, k2=v2)" → (name, {k:v}). A bare "input(temperature)" yields {temperature: 0}.
    private static (string Name, Dictionary<string, double> Args) ParseCall(string stage)
    {
        var open = stage.IndexOf('(');
        var close = stage.LastIndexOf(')');
        if (open < 0 || close < open) throw new FormatException($"malformed stage '{stage}' — expected name(args)");

        var name = stage[..open].Trim();
        var body = stage[(open + 1)..close].Trim();
        var args = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (body.Length == 0) return (name, args);

        foreach (var part in body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) { args[part] = 0; continue; } // a bare token (e.g. input(temperature)) → the port name
            var key = part[..eq].Trim();
            var valueText = part[(eq + 1)..].Trim();
            if (!double.TryParse(valueText, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"'{valueText}' is not a number in stage '{stage}'");
            args[key] = value;
        }
        return (name, args);
    }
}

/// <summary>
/// A composite references a block type that the resolver cannot (yet) supply. Distinct from a plain
/// <see cref="FormatException"/> so catalog registration can tell "defer — the referenced type may be another
/// composite not registered yet" (roadmap Epic 1H E2, composite-in-composite) from "drop — malformed DSL".
/// </summary>
public sealed class UnknownBlockTypeException : FormatException
{
    public UnknownBlockTypeException(string typeId)
        : base($"unknown block type '{typeId}'") => TypeId = typeId;

    public string TypeId { get; }
}
