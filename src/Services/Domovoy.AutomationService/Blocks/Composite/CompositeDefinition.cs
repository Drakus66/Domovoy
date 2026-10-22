// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

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
    IReadOnlyList<CompositeNode> Nodes,
    IReadOnlyList<CompositeParam>? Params = null);

/// <summary>
/// A composite-level tunable param (roadmap Epic 2Q — <b>parameter passthrough</b>) that surfaces a specific
/// internal node param on the composite instance, so a ready-made composite can be tuned without editing the
/// definition. The composite type exposes it as an ordinary <see cref="Domovoy.AutomationService.Blocks.BlockParamSpec"/>;
/// at tick time the instance's value wins, falling back to <see cref="Default"/> — which the DSL defaults to the
/// node's authored value, so exposing a param never changes out-of-the-box behaviour. DSL:
/// <c>param setpoint = h.high default 21.5</c>.
/// </summary>
/// <param name="Name">Exposed param name on the composite instance (e.g. <c>setpoint</c>).</param>
/// <param name="Default">Default value — pre-fills the authoring form and is the tick-time fallback.</param>
/// <param name="Description">Human description for the authoring form.</param>
/// <param name="TargetNode">Internal node id whose param this drives.</param>
/// <param name="TargetParam">The internal node param key this overrides.</param>
public sealed record CompositeParam(
    string Name, double Default, string Description, string TargetNode, string TargetParam);

/// <summary>An internal node: an instance of a built-in (primitive) type with fixed params and wired inputs.</summary>
/// <param name="Id">Node id, unique within the composite (the DSL assigns n0, n1, …).</param>
/// <param name="TypeId">The built-in type this node instantiates.</param>
/// <param name="Params">Numeric params for the node (numeric-only, like <see cref="Domovoy.Contracts.Blocks.ControlBlock.Params"/>).</param>
/// <param name="Inputs">Port → source. A source is <c>"$name"</c> (a composite input) or <c>"nodeId#capId"</c> (another node's output).</param>
/// <param name="Options">Non-numeric (enum/bool/text) options for the node (roadmap Epic 2Q) — e.g. a comparator's <c>op</c>.</param>
public sealed record CompositeNode(
    string Id,
    string TypeId,
    IReadOnlyDictionary<string, double> Params,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyDictionary<string, string>? Options = null);

/// <summary>An exposed composite input port, bridged to the parent block context.</summary>
public sealed record CompositeInput(string Name, CapabilityKind Kind, string Description);

/// <summary>An exposed composite output: a capability fed from an internal node's output.</summary>
/// <param name="Source"><c>"nodeId#capId"</c> — the internal signal that drives this output.</param>
public sealed record CompositeOutput(Capability Capability, string Source);
