// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Blocks.Composite;

/// <summary>
/// A composite block <b>type</b> (roadmap Epic 1H E2): a first-class <see cref="IBlockType"/> built from a
/// <see cref="CompositeDefinition"/>, so a composite plugs into the catalog + runtime exactly like a built-in —
/// but a new one is data (a DSL document), not a C# class. Its schema (ports/outputs) is derived from the
/// definition; <see cref="Create"/> materializes the internal primitive instances via the supplied resolver.
/// </summary>
public sealed class CompositeBlockType : IBlockType
{
    private readonly CompositeDefinition _def;
    private readonly Func<string, IBlockType?> _resolve;

    public CompositeBlockType(CompositeDefinition def, Func<string, IBlockType?> resolveType)
    {
        _def = def;
        _resolve = resolveType;
        Inputs = def.Inputs.Select(i => new BlockPortSpec(i.Name, i.Kind, i.Description)).ToList();
        Outputs = def.Outputs.Select(o => o.Capability).ToList();
        Params = (def.Params ?? Array.Empty<CompositeParam>())
            .Select(p => new BlockParamSpec(p.Name, p.Default, null, null, null, p.Description))
            .ToList();
    }

    public string TypeId => _def.TypeId;
    // Composites are ready-made recipes — surfaced under "templates" in the authoring picker (Epic 2Q).
    public string Category => BlockCategories.Template;
    public string Title => _def.Title;
    public string Description => _def.Description;
    public IReadOnlyList<BlockPortSpec> Inputs { get; }
    public IReadOnlyList<Capability> Outputs { get; }

    // Epic 2Q parameter passthrough: exposed composite params surface as ordinary tunable params, so the
    // authoring form (which renders IBlockType.Params generically) lets an instance override an internal
    // node param — no UI change. Composites without exposed params keep an empty list (params baked on nodes).
    public IReadOnlyList<BlockParamSpec> Params { get; }

    public IBlock Create() => new CompositeBlock(_def, _resolve);
}

/// <summary>
/// Runs a composite's internal subgraph on each tick (roadmap Epic 1H E2). It ticks the child primitives in
/// declared order over an internal blackboard: each child reads its wired inputs (a composite input, bridged to
/// the parent context, or another child's output from the blackboard) and emits to the blackboard; finally the
/// composite's outputs are mapped from the blackboard onto the parent context. Internal signals persist across
/// ticks (via the parent state bag) so feedback/estimator chains work; each child's own state is namespaced by
/// node id, so two nodes of the same type keep separate state.
/// </summary>
public sealed class CompositeBlock : IBlock
{
    private readonly CompositeDefinition _def;
    private readonly List<(CompositeNode Node, IBlock Block)> _children = new();

    /// <summary>
    /// Passthrough overrides (Epic 2Q), keyed "nodeId#paramKey" → the exposed composite param name. When an
    /// internal node reads that param, the composite instance's value wins over the node's authored value.
    /// </summary>
    private readonly IReadOnlyDictionary<string, string> _paramOverrides;

    public CompositeBlock(CompositeDefinition def, Func<string, IBlockType?> resolveType)
    {
        _def = def;
        foreach (var node in def.Nodes)
        {
            var type = resolveType(node.TypeId)
                ?? throw new InvalidOperationException($"composite '{def.TypeId}' references unknown type '{node.TypeId}'");
            _children.Add((node, type.Create()));
        }

        _paramOverrides = (def.Params ?? Array.Empty<CompositeParam>())
            .ToDictionary(p => $"{p.TargetNode}#{p.TargetParam}", p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    public void Tick(IBlockContext ctx)
    {
        // Internal blackboard, keyed "nodeId#capId"; persisted so a downstream/feedback node sees last round.
        var signals = ctx.GetState<Dictionary<string, object?>>("__signals") ?? new Dictionary<string, object?>();

        foreach (var (node, block) in _children)
            block.Tick(new InternalContext(node, ctx, signals, _paramOverrides));

        // Map the composite's outputs from the internal signals onto the parent context.
        foreach (var output in _def.Outputs)
            if (signals.TryGetValue(output.Source, out var v) && v is not null)
                ctx.Emit(output.Capability.Id, v);

        ctx.SetState("__signals", signals);
    }

    /// <summary>The per-child view: bridges wired inputs, namespaces state, delegates the clock/commands to the parent.</summary>
    private sealed class InternalContext : IBlockContext
    {
        private readonly CompositeNode _node;
        private readonly IBlockContext _parent;
        private readonly Dictionary<string, object?> _signals;
        private readonly IReadOnlyDictionary<string, string> _paramOverrides;

        public InternalContext(
            CompositeNode node, IBlockContext parent, Dictionary<string, object?> signals,
            IReadOnlyDictionary<string, string> paramOverrides)
        {
            _node = node;
            _parent = parent;
            _signals = signals;
            _paramOverrides = paramOverrides;
        }

        public DateTimeOffset Now => _parent.Now;
        public string? ZoneId => _parent.ZoneId;
        public string? ZoneKind => _parent.ZoneKind;

        public object? Read(string inputPort)
        {
            if (!_node.Inputs.TryGetValue(inputPort, out var source)) return null;
            // "$name" → a composite input (bridge to the parent); "nodeId#capId" → an internal signal.
            if (source.StartsWith('$')) return _parent.Read(source[1..]);
            return _signals.TryGetValue(source, out var v) ? v : null;
        }

        public double? ReadNumber(string inputPort) => Read(inputPort) switch
        {
            double d => d,
            bool b => b ? 1 : 0,
            int i => i,
            long l => l,
            _ => null,
        };

        public double Param(string key, double fallback)
        {
            // The node's authored value is the baseline (and the fallback for an exposed param the instance
            // hasn't overridden). Epic 2Q passthrough: if this param is exposed on the composite, the instance's
            // value (read from the parent context's params) wins over the authored one.
            var authored = _node.Params.TryGetValue(key, out var v) ? v : fallback;
            return _paramOverrides.TryGetValue($"{_node.Id}#{key}", out var external)
                ? _parent.Param(external, authored)
                : authored;
        }

        // Epic 2Q: node string options (enum/bool/text), so option-driven primitives work inside composites.
        public string? Option(string key) => _node.Options is not null && _node.Options.TryGetValue(key, out var v) ? v : null;

        // Commands to the composite's virtual device reach a node output of the same id (e.g. a setpoint).
        public object? Commanded(string capabilityId) => _parent.Commanded(capabilityId);

        public void Emit(string capabilityId, object? value) => _signals[$"{_node.Id}#{capabilityId}"] = value;

        public T? GetState<T>(string key) => _parent.GetState<T>($"{_node.Id}.{key}");
        public void SetState<T>(string key, T value) => _parent.SetState($"{_node.Id}.{key}", value);

        public void Log(string message) => _parent.Log($"[{_node.Id}] {message}");
    }
}
