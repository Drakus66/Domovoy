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
/// no code, no rebuild. v1 supports the linear pipe form (the common ~80% case); branching composites are a
/// later extension of the same document model.
/// </summary>
public static class BlockDsl
{
    /// <summary>
    /// Parse a pipe pipeline into a composite definition. <paramref name="resolveType"/> supplies the primitive
    /// types (for primary port/output discovery). Throws <see cref="FormatException"/> on malformed input.
    /// </summary>
    public static CompositeDefinition Parse(string typeId, string title, string description, string dsl, Func<string, IBlockType?> resolveType)
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
            var type = resolveType(nodeType) ?? throw new FormatException($"unknown block type '{nodeType}'");
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
