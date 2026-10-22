// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Updater.Model;

namespace Domovoy.Updater.Services;

/// <summary>
/// Turns "I want to update X" into an ordered, executable plan (roadmap Epic 3K).
///
/// <para><b>Why this exists.</b> Domovoy updates per component: bumping the ML service must not drag
/// the whole system along. But components do depend on each other, so a point update sometimes has
/// to pull others in — and the owner deserves to see exactly who and why before pressing anything.</para>
///
/// <para><b>Dependencies name interfaces, not services.</b> A component declares
/// <c>requires: db-api >= 5</c>, never "db-gateway >= 4.3". Each interface has exactly one provider,
/// so the arbitrary service-to-service graph — the one that deadlocks — never forms.</para>
///
/// <para><b>Cycles are not deadlocks.</b> Mutual requirements mean the intermediate state is
/// incompatible, i.e. those components must move <i>together</i>. That is a strongly connected
/// component in the graph, found with Tarjan and executed as one atomic group. The only real refusal
/// is "no combination of versions in this channel satisfies the constraints", and it names the
/// conflicting pair.</para>
///
/// <para><b>Termination.</b> Each fix-point iteration either adds a component to the plan or raises
/// one component's target version, both bounded by what the channel offers, over a finite component
/// set — so it converges in at most O(components) rounds. The hard cap below is a backstop against
/// a malformed registry, not part of the algorithm.</para>
///
/// Pure: no I/O, no clock, no Docker. Everything it needs is passed in, which is what makes the
/// interesting cases (cycles, laggards, refusals) cheap to test.
/// </summary>
public sealed class DependencyResolver
{
    private const int MaxIterations = 64;

    private readonly IReadOnlyDictionary<string, InstalledComponent> _installed;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<AvailableComponent>> _available;

    public DependencyResolver(
        IReadOnlyDictionary<string, InstalledComponent> installed,
        IReadOnlyDictionary<string, IReadOnlyList<AvailableComponent>> available)
    {
        _installed = installed;
        _available = available;
    }

    /// <summary>Update everything that has a newer version on the channel.</summary>
    public UpdatePlan ResolveAll()
    {
        var goals = _installed.Keys
            .Where(name => Newest(name) is { } newest && SemVerComparer.IsNewer(newest.Version, _installed[name].Version))
            .ToList();
        return Resolve(goals);
    }

    /// <summary>Update the named components to the newest version on the channel, plus whatever that requires.</summary>
    public UpdatePlan Resolve(IReadOnlyCollection<string> goals)
    {
        // target: компонент → выбранная версия. Это и есть строящийся план.
        var target = new Dictionary<string, AvailableComponent>();

        foreach (var goal in goals)
        {
            var newest = Newest(goal);
            if (newest is null)
                return UpdatePlan.Refuse($"Для компонента '{goal}' нет доступных версий в канале.", goal);

            if (_installed.TryGetValue(goal, out var cur) && !SemVerComparer.IsNewer(newest.Version, cur.Version))
                continue; // уже на новейшей — цель достигнута, в план не кладём

            target[goal] = newest;
        }

        if (target.Count == 0) return UpdatePlan.Empty();

        var reasons = target.Keys.ToDictionary(k => k, _ => "запрошено обновление");

        for (var iteration = 0; ; iteration++)
        {
            if (iteration > MaxIterations)
                return UpdatePlan.Refuse("Решатель не сошёлся — вероятно, повреждены метаданные образов в реестре.");

            var changed = false;

            // (a) requires компонентов плана должны быть удовлетворены.
            var forward = SatisfyForward(target, reasons, ref changed);
            if (forward is not null) return forward;

            // (b) установленные потребители, которых ломает новая версия провайдера, втягиваются в план.
            var backward = SatisfyBackward(target, reasons, ref changed);
            if (backward is not null) return backward;

            // (c) версия шины: набор совместим, когда max(understands) <= min(speaks).
            var bus = SatisfyBus(target, reasons, ref changed);
            if (bus is not null) return bus;

            if (!changed) break;
        }

        return BuildGroups(target, reasons);
    }

    // ---------------------------------------------------------------- фаза (a)

    private UpdatePlan? SatisfyForward(
        Dictionary<string, AvailableComponent> target,
        Dictionary<string, string> reasons,
        ref bool changed)
    {
        foreach (var (name, candidate) in target.ToList())
        {
            foreach (var (iface, required) in candidate.Deps.Requires)
            {
                var providerName = FindProvider(iface, target);
                if (providerName is null)
                    return UpdatePlan.Refuse(
                        $"'{name}' требует интерфейс '{iface}', которого никто не предоставляет.", name);

                if (Satisfies(EffectiveDeps(providerName, target), iface, required)) continue;

                var fix = NewestSatisfying(providerName, iface, required);
                if (fix is null)
                    return UpdatePlan.Refuse(
                        $"'{name}' требует {iface} ≥ {required}, но в канале нет версии " +
                        $"'{providerName}', которая это даёт.",
                        name, providerName);

                if (target.TryGetValue(providerName, out var already)
                    && !SemVerComparer.IsNewer(fix.Version, already.Version))
                    continue;

                target[providerName] = fix;
                reasons[providerName] = $"{name} требует {iface} ≥ {required}";
                changed = true;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- фаза (b)

    private UpdatePlan? SatisfyBackward(
        Dictionary<string, AvailableComponent> target,
        Dictionary<string, string> reasons,
        ref bool changed)
    {
        foreach (var (name, installed) in _installed)
        {
            if (target.ContainsKey(name)) continue;

            foreach (var (iface, required) in installed.Deps.Requires)
            {
                var providerName = FindProvider(iface, target);
                if (providerName is null || !target.ContainsKey(providerName)) continue;
                if (Satisfies(EffectiveDeps(providerName, target), iface, required)) continue;

                // Новая версия провайдера этого потребителя ломает — значит он тоже должен поехать.
                var fix = NewestFor(name, c =>
                    !c.Deps.Requires.TryGetValue(iface, out var need)
                    || Satisfies(target[providerName].Deps, iface, need));

                if (fix is null)
                    return UpdatePlan.Refuse(
                        $"Обновление '{providerName}' ломает '{name}' (нужен {iface} ≥ {required}), " +
                        "а совместимой версии в канале нет.",
                        providerName, name);

                target[name] = fix;
                reasons[name] = $"обновление '{providerName}' иначе ломает совместимость по {iface}";
                changed = true;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- фаза (c)

    private UpdatePlan? SatisfyBus(
        Dictionary<string, AvailableComponent> target,
        Dictionary<string, string> reasons,
        ref bool changed)
    {
        var onBus = _installed.Keys
            .Select(name => (Name: name, Bus: EffectiveDeps(name, target)?.Bus))
            .Where(x => x.Bus is not null)
            .ToList();

        if (onBus.Count == 0) return null;

        var maxUnderstands = onBus.Max(x => x.Bus!.Understands);

        foreach (var (name, bus) in onBus)
        {
            if (bus!.Speaks >= maxUnderstands) continue;

            // Кто-то в наборе понимает контракт только с версии N, а этот всё ещё говорит на меньшей —
            // ломающее изменение контрактов. Именно здесь «релизный поезд» и выводится сам собой.
            var fix = NewestFor(name, c => c.Deps.Bus is not null && c.Deps.Bus.Speaks >= maxUnderstands);
            if (fix is null)
                return UpdatePlan.Refuse(
                    $"Набор несовместим по шине: требуется версия контракта ≥ {maxUnderstands}, " +
                    $"а для '{name}' такой версии в канале нет.",
                    name);

            if (target.TryGetValue(name, out var already) && !SemVerComparer.IsNewer(fix.Version, already.Version))
                continue;

            target[name] = fix;
            reasons[name] = $"ломающее изменение контрактов шины (требуется версия ≥ {maxUnderstands})";
            changed = true;
        }

        return null;
    }

    // ---------------------------------------------------------------- граф и группы

    private UpdatePlan BuildGroups(
        Dictionary<string, AvailableComponent> target,
        Dictionary<string, string> reasons)
    {
        var names = target.Keys.ToList();
        var index = names.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
        var edges = names.Select(_ => new HashSet<int>()).ToList();

        foreach (var name in names)
        {
            foreach (var (iface, required) in target[name].Deps.Requires)
            {
                var providerName = FindProvider(iface, target);
                if (providerName is null || !index.ContainsKey(providerName)) continue;

                // Ребро порядка: провайдер обновляется раньше потребителя.
                edges[index[providerName]].Add(index[name]);

                // Ребро атомарности: новый провайдер перестаёт обслуживать ТЕКУЩУЮ версию потребителя,
                // значит промежуточное состояние нерабочее — обновлять только вместе.
                var installedRequirement = _installed.TryGetValue(name, out var cur)
                    && cur.Deps.Requires.TryGetValue(iface, out var r) ? r : required;

                if (!Satisfies(target[providerName].Deps, iface, installedRequirement))
                    edges[index[name]].Add(index[providerName]);
            }
        }

        // Ломающее изменение шины делает промежуточные состояния нерабочими для всех её участников.
        var busBreaking = names.Any(n =>
            target[n].Deps.Bus is { } b
            && _installed.TryGetValue(n, out var cur)
            && cur.Deps.Bus is { } old
            && b.Understands > old.Understands);

        if (busBreaking)
        {
            var busNames = names.Where(n => target[n].Deps.Bus is not null).Select(n => index[n]).ToList();
            foreach (var a in busNames)
                foreach (var b in busNames)
                    if (a != b) edges[a].Add(b);
        }

        var sccs = Tarjan(edges);

        // Конденсация даёт DAG; Тарьян выдаёт SCC в обратном топологическом порядке,
        // поэтому достаточно развернуть список.
        var groups = sccs
            .AsEnumerable()
            .Reverse()
            .Select(scc => new UpdateGroup(
                scc.OrderBy(i => ReleaseSource.OrderOf(names[i]))
                   .Select(i => ToPlanned(names[i], target[names[i]], reasons))
                   .ToList(),
                Atomic: scc.Count > 1))
            .ToList();

        return new UpdatePlan { Groups = groups };
    }

    private PlannedUpdate ToPlanned(
        string name, AvailableComponent candidate, IReadOnlyDictionary<string, string> reasons) =>
        new(
            Component: name,
            Container: candidate.Deps.Container is { Length: > 0 } c ? c : ReleaseSource.ContainerOf(name),
            FromVersion: _installed.TryGetValue(name, out var cur) ? cur.Version : "—",
            ToVersion: candidate.Version,
            Repository: candidate.Repository,
            Digest: candidate.Digest,
            Reason: reasons.TryGetValue(name, out var r) ? r : "обновление зависимости");

    /// <summary>
    /// Tarjan's strongly-connected components. A component of size &gt; 1 is exactly a set that must
    /// move atomically — either mutual requirements, or an edge we added because the intermediate
    /// state would be incompatible. Iterative rather than recursive: the graph is small, but a stack
    /// overflow inside the updater is the one crash that would leave a half-updated house.
    /// </summary>
    internal static List<List<int>> Tarjan(IReadOnlyList<HashSet<int>> edges)
    {
        var n = edges.Count;
        var index = new int[n];
        var low = new int[n];
        var onStack = new bool[n];
        var stack = new Stack<int>();
        var result = new List<List<int>>();
        var counter = 1;

        Array.Fill(index, 0); // 0 = не посещён

        for (var start = 0; start < n; start++)
        {
            if (index[start] != 0) continue;

            var work = new Stack<(int Node, IEnumerator<int> Edges)>();
            index[start] = low[start] = counter++;
            stack.Push(start);
            onStack[start] = true;
            work.Push((start, edges[start].OrderBy(x => x).GetEnumerator()));

            while (work.Count > 0)
            {
                var (node, it) = work.Peek();

                if (it.MoveNext())
                {
                    var next = it.Current;
                    if (index[next] == 0)
                    {
                        index[next] = low[next] = counter++;
                        stack.Push(next);
                        onStack[next] = true;
                        work.Push((next, edges[next].OrderBy(x => x).GetEnumerator()));
                    }
                    else if (onStack[next])
                    {
                        low[node] = Math.Min(low[node], index[next]);
                    }
                    continue;
                }

                work.Pop();
                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[node]);
                }

                if (low[node] == index[node])
                {
                    var scc = new List<int>();
                    int member;
                    do
                    {
                        member = stack.Pop();
                        onStack[member] = false;
                        scc.Add(member);
                    } while (member != node);
                    result.Add(scc);
                }
            }
        }

        return result;
    }

    // ---------------------------------------------------------------- вспомогательное

    private static bool Satisfies(ComponentDeps? provider, string iface, int required) =>
        provider is not null
        && provider.Provides.TryGetValue(iface, out var p)
        && required >= p.MinCompat
        && required <= p.Version;

    /// <summary>Deps of a component as they will be after the plan is applied.</summary>
    private ComponentDeps? EffectiveDeps(string name, IReadOnlyDictionary<string, AvailableComponent> target) =>
        target.TryGetValue(name, out var planned) ? planned.Deps
        : _installed.TryGetValue(name, out var installed) ? installed.Deps
        : null;

    /// <summary>Which component owns an interface, looking at both the installed set and the plan.</summary>
    private string? FindProvider(string iface, IReadOnlyDictionary<string, AvailableComponent> target)
    {
        foreach (var (name, c) in _installed)
            if (EffectiveDeps(name, target)?.Provides.ContainsKey(iface) == true) return name;

        foreach (var (name, c) in target)
            if (c.Deps.Provides.ContainsKey(iface)) return name;

        // Компонент мог быть ещё не установлен (новый в этом релизе) — ищем среди доступного.
        foreach (var (name, versions) in _available)
            if (versions.Any(v => v.Deps.Provides.ContainsKey(iface))) return name;

        return null;
    }

    private AvailableComponent? Newest(string name) =>
        _available.TryGetValue(name, out var list) && list.Count > 0
            ? list.OrderBy(v => v.Version, SemVerComparer.Instance).Last()
            : null;

    private AvailableComponent? NewestFor(string name, Func<AvailableComponent, bool> predicate) =>
        _available.TryGetValue(name, out var list)
            ? list.Where(predicate).OrderBy(v => v.Version, SemVerComparer.Instance).LastOrDefault()
            : null;

    private AvailableComponent? NewestSatisfying(string name, string iface, int required) =>
        NewestFor(name, c => Satisfies(c.Deps, iface, required));
}
