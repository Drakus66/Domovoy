using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Blocks.Composite;

/// <summary>
/// Declarative definition of an E2 composite block (roadmap Epic 1H) — a subgraph of built-in primitives wired
/// together, expressed as data (no code, no rebuild). A new composite type is one of these documents (authored
/// via the pipe DSL, <see cref="BlockDsl"/>), loaded into the catalog like any built-in type. The runtime ticks
/// the internal nodes in declared order over an internal blackboard; the composite's external ports bridge to the
/// parent block context.
/// </summary>
public sealed record CompositeDefinition(
    string TypeId,
    string Title,
    string Description,
    IReadOnlyList<CompositeInput> Inputs,
    IReadOnlyList<CompositeOutput> Outputs,
    IReadOnlyList<CompositeNode> Nodes);

/// <summary>An internal node: an instance of a built-in (primitive) type with fixed params and wired inputs.</summary>
/// <param name="Id">Node id, unique within the composite (the DSL assigns n0, n1, …).</param>
/// <param name="TypeId">The built-in type this node instantiates.</param>
/// <param name="Params">Numeric params for the node (numeric-only, like <see cref="Domovoy.Contracts.Blocks.ControlBlock.Params"/>).</param>
/// <param name="Inputs">Port → source. A source is <c>"$name"</c> (a composite input) or <c>"nodeId#capId"</c> (another node's output).</param>
public sealed record CompositeNode(
    string Id,
    string TypeId,
    IReadOnlyDictionary<string, double> Params,
    IReadOnlyDictionary<string, string> Inputs);

/// <summary>An exposed composite input port, bridged to the parent block context.</summary>
public sealed record CompositeInput(string Name, CapabilityKind Kind, string Description);

/// <summary>An exposed composite output: a capability fed from an internal node's output.</summary>
/// <param name="Source"><c>"nodeId#capId"</c> — the internal signal that drives this output.</param>
public sealed record CompositeOutput(Capability Capability, string Source);
