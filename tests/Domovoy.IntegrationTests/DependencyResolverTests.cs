// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Updater.Model;
using Domovoy.Updater.Services;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// The update resolver (roadmap Epic 3K). This is the piece that decides what a "update just the ML
/// service" press actually does on someone's house, so it is tested densely — and it can be, because
/// it is pure: installed set in, available set in, plan out. No Docker, no registry, no clock.
///
/// <para>The cases below are the ones that matter in practice: a point update that touches nothing
/// else, a dependency that pulls a provider in, a live consumer that would be broken by that
/// provider, a breaking bus change that necessarily moves everything, mutual requirements (which must
/// become one atomic group rather than a deadlock), and an honest refusal when the channel simply has
/// no compatible combination.</para>
/// </summary>
public class DependencyResolverTests
{
    // ---------------------------------------------------------------- helpers

    private static ComponentDeps Deps(
        string name,
        string version,
        (int Speaks, int Understands)? bus = null,
        (string Iface, int Version, int MinCompat)? provides = null,
        params (string Iface, int Version)[] requires) =>
        new()
        {
            Component = name,
            Container = name,
            Version = version,
            Bus = bus is { } b ? new BusCompatibility { Speaks = b.Speaks, Understands = b.Understands } : null,
            Provides = provides is { } p
                ? new Dictionary<string, ProvidedInterface>
                {
                    [p.Iface] = new() { Version = p.Version, MinCompat = p.MinCompat },
                }
                : new Dictionary<string, ProvidedInterface>(),
            Requires = requires.ToDictionary(r => r.Iface, r => r.Version),
        };

    private static InstalledComponent Installed(ComponentDeps deps) =>
        new(deps.Component, deps.Container, $"drakus66/domovoy-{deps.Component}", "dev",
            $"sha256:{deps.Component}-{deps.Version}", deps.Version, deps);

    private static AvailableComponent Available(ComponentDeps deps) =>
        new(deps.Component, $"drakus66/domovoy-{deps.Component}", deps.Version,
            $"sha256:{deps.Component}-{deps.Version}", deps.Version, deps);

    private static Dictionary<string, IReadOnlyList<AvailableComponent>> Channel(
        params ComponentDeps[] versions) =>
        versions
            .GroupBy(v => v.Component)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AvailableComponent>)g.Select(Available).ToList());

    // ---------------------------------------------------------------- cases

    [Fact]
    public void PointUpdate_TouchesNothingElse()
    {
        // Самый частый и самый ценный случай: обновился один сервис, его зависимости уже удовлетворены —
        // остальной дом не трогаем вообще.
        var installed = new Dictionary<string, InstalledComponent>
        {
            ["db-gateway"] = Installed(Deps("db-gateway", "1.0.10", provides: ("db-api", 4, 1))),
            ["automation-service"] = Installed(Deps("automation-service", "1.0.10", requires: ("db-api", 4))),
        };

        var available = Channel(
            Deps("db-gateway", "1.0.10", provides: ("db-api", 4, 1)),
            Deps("automation-service", "1.0.11", requires: ("db-api", 4)));

        var plan = new DependencyResolver(installed, available).Resolve(new[] { "automation-service" });

        Assert.True(plan.Ok);
        var move = Assert.Single(plan.All);
        Assert.Equal("automation-service", move.Component);
        Assert.Equal("1.0.11", move.ToVersion);
    }

    [Fact]
    public void RaisedRequirement_PullsProviderIn_AndOrdersItFirst()
    {
        var installed = new Dictionary<string, InstalledComponent>
        {
            ["db-gateway"] = Installed(Deps("db-gateway", "1.0.10", provides: ("db-api", 4, 1))),
            ["automation-service"] = Installed(Deps("automation-service", "1.0.10", requires: ("db-api", 4))),
        };

        // Новая версия потребителя требует db-api 5 — провайдер обязан поехать первым.
        var available = Channel(
            Deps("db-gateway", "1.0.12", provides: ("db-api", 5, 1)),
            Deps("automation-service", "1.0.11", requires: ("db-api", 5)));

        var plan = new DependencyResolver(installed, available).Resolve(new[] { "automation-service" });

        Assert.True(plan.Ok);
        Assert.Equal(2, plan.All.Count);
        Assert.Equal("db-gateway", plan.All[0].Component);
        Assert.Equal("automation-service", plan.All[1].Component);
        Assert.Contains("db-api", plan.All[0].Reason);
    }

    [Fact]
    public void ProviderRaisingMinCompat_PullsLiveConsumerIn_AsOneAtomicGroup()
    {
        // Провайдер перестал обслуживать старых потребителей (minCompat вырос). Промежуточное
        // состояние нерабочее, поэтому обновляться они обязаны вместе, а не по очереди.
        var installed = new Dictionary<string, InstalledComponent>
        {
            ["db-gateway"] = Installed(Deps("db-gateway", "1.0.10", provides: ("db-api", 4, 1))),
            ["automation-service"] = Installed(Deps("automation-service", "1.0.10", requires: ("db-api", 4))),
        };

        var available = Channel(
            Deps("db-gateway", "2.0.1", provides: ("db-api", 5, 5)),
            Deps("automation-service", "2.0.1", requires: ("db-api", 5)));

        var plan = new DependencyResolver(installed, available).Resolve(new[] { "db-gateway" });

        Assert.True(plan.Ok);
        Assert.Equal(2, plan.All.Count);
        var group = Assert.Single(plan.Groups);
        Assert.True(group.Atomic);
    }

    [Fact]
    public void BreakingBusChange_MovesEveryoneOnTheBus()
    {
        // «Релизный поезд» здесь не отдельный режим, а вывод: новая версия понимает контракт только
        // с v2, поэтому все, кто говорит на v1, обязаны поехать следом.
        var installed = new Dictionary<string, InstalledComponent>
        {
            ["db-gateway"] = Installed(Deps("db-gateway", "1.0.10", bus: (1, 1))),
            ["automation-service"] = Installed(Deps("automation-service", "1.0.10", bus: (1, 1))),
            ["connectivity-service"] = Installed(Deps("connectivity-service", "1.0.10", bus: (1, 1))),
        };

        var available = Channel(
            Deps("db-gateway", "2.0.1", bus: (2, 2)),
            Deps("automation-service", "2.0.1", bus: (2, 2)),
            Deps("connectivity-service", "2.0.1", bus: (2, 2)));

        var plan = new DependencyResolver(installed, available).Resolve(new[] { "db-gateway" });

        Assert.True(plan.Ok);
        Assert.Equal(3, plan.All.Count);
        Assert.Contains(plan.All, m => m.Component == "connectivity-service");
        // Ломающее изменение контракта делает промежуточные состояния нерабочими — значит атомарно.
        Assert.All(plan.Groups, g => Assert.True(g.Atomic));
    }

    [Fact]
    public void MutualRequirements_BecomeOneAtomicGroup_NotADeadlock()
    {
        // Ровно тот случай, ради которого в решателе есть SCC: A требует новый B, B требует новый A.
        // Это не тупик, а требование атомарности.
        var installed = new Dictionary<string, InstalledComponent>
        {
            ["api-gateway"] = Installed(
                Deps("api-gateway", "1.0.10", provides: ("rest-api", 6, 1), requires: ("automation-api", 3))),
            ["automation-service"] = Installed(
                Deps("automation-service", "1.0.10", provides: ("automation-api", 3, 1), requires: ("rest-api", 6))),
        };

        var available = Channel(
            Deps("api-gateway", "2.0.1", provides: ("rest-api", 7, 7), requires: ("automation-api", 4)),
            Deps("automation-service", "2.0.1", provides: ("automation-api", 4, 4), requires: ("rest-api", 7)));

        var plan = new DependencyResolver(installed, available).Resolve(new[] { "api-gateway" });

        Assert.True(plan.Ok);
        var group = Assert.Single(plan.Groups);
        Assert.True(group.Atomic);
        Assert.Equal(2, group.Members.Count);
    }

    [Fact]
    public void NoCompatibleCombination_RefusesAndNamesTheConflict()
    {
        // Единственный настоящий отказ. Он обязан быть внятным: кто с кем не сходится.
        var installed = new Dictionary<string, InstalledComponent>
        {
            ["db-gateway"] = Installed(Deps("db-gateway", "1.0.10", provides: ("db-api", 4, 1))),
            ["automation-service"] = Installed(Deps("automation-service", "1.0.10", requires: ("db-api", 4))),
        };

        // Потребителю нужен db-api 9, а в канале такого db-gateway нет.
        var available = Channel(
            Deps("db-gateway", "1.0.12", provides: ("db-api", 5, 1)),
            Deps("automation-service", "3.0.1", requires: ("db-api", 9)));

        var plan = new DependencyResolver(installed, available).Resolve(new[] { "automation-service" });

        Assert.False(plan.Ok);
        Assert.NotNull(plan.Refusal);
        Assert.Contains("automation-service", plan.Conflict);
        Assert.Contains("db-gateway", plan.Conflict);
    }

    [Fact]
    public void TopologyRequirement_IsJustAnotherDependency()
    {
        // Изменение compose (новый env/том) — не отдельный «гейт», а обычная зависимость:
        // компонент требует topology >= 2, и бандл едет раньше него.
        var installed = new Dictionary<string, InstalledComponent>
        {
            ["topology"] = Installed(Deps("topology", "1.0", provides: ("topology", 1, 1))),
            ["automation-service"] = Installed(Deps("automation-service", "1.0.10", requires: ("topology", 1))),
        };

        var available = Channel(
            Deps("topology", "2.0", provides: ("topology", 2, 1)),
            Deps("automation-service", "1.0.11", requires: ("topology", 2)));

        var plan = new DependencyResolver(installed, available).Resolve(new[] { "automation-service" });

        Assert.True(plan.Ok);
        Assert.Equal("topology", plan.All[0].Component);
        Assert.Equal("automation-service", plan.All[1].Component);
    }

    [Fact]
    public void AlreadyCurrent_ProducesAnEmptyPlan()
    {
        var installed = new Dictionary<string, InstalledComponent>
        {
            ["db-gateway"] = Installed(Deps("db-gateway", "1.0.10", provides: ("db-api", 4, 1))),
        };

        var plan = new DependencyResolver(installed, Channel(Deps("db-gateway", "1.0.10", provides: ("db-api", 4, 1))))
            .Resolve(new[] { "db-gateway" });

        Assert.True(plan.Ok);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void ResolveAll_TakesEverythingThatHasSomethingNewer()
    {
        var installed = new Dictionary<string, InstalledComponent>
        {
            ["db-gateway"] = Installed(Deps("db-gateway", "1.0.10", provides: ("db-api", 4, 1))),
            ["webui"] = Installed(Deps("webui", "1.0.10")),
            ["connectivity-service"] = Installed(Deps("connectivity-service", "1.0.10")),
        };

        var available = Channel(
            Deps("db-gateway", "1.0.11", provides: ("db-api", 4, 1)),
            Deps("webui", "1.0.12"),
            Deps("connectivity-service", "1.0.10")); // без изменений — в план попасть не должен

        var plan = new DependencyResolver(installed, available).ResolveAll();

        Assert.True(plan.Ok);
        Assert.Equal(2, plan.All.Count);
        Assert.DoesNotContain(plan.All, m => m.Component == "connectivity-service");
    }

    [Fact]
    public void LongDependencyChain_Converges()
    {
        // Проверка завершаемости на цепочке: каждый следующий требует предыдущего.
        var installed = new Dictionary<string, InstalledComponent>();
        var versions = new List<ComponentDeps>();

        for (var i = 0; i < 6; i++)
        {
            var name = $"svc-{i}";
            var provides = ($"iface-{i}", 1, 1);
            var requires = i > 0 ? new[] { ($"iface-{i - 1}", 1) } : Array.Empty<(string, int)>();

            installed[name] = Installed(Deps(name, "1.0.0", provides: provides, requires: requires));
            versions.Add(Deps(name, "1.0.1",
                provides: ($"iface-{i}", 2, 1),
                requires: i > 0 ? new[] { ($"iface-{i - 1}", 2) } : Array.Empty<(string, int)>()));
        }

        var plan = new DependencyResolver(installed, Channel(versions.ToArray())).Resolve(new[] { "svc-5" });

        Assert.True(plan.Ok);
        Assert.Equal(6, plan.All.Count);
        // Провайдеры раньше потребителей на всей длине цепочки.
        var order = plan.All.Select(m => m.Component).ToList();
        for (var i = 0; i < 6; i++) Assert.Equal($"svc-{i}", order[i]);
    }
}
