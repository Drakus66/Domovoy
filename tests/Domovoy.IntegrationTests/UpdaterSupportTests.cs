// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Endpoints;
using Domovoy.Updater.Model;
using Domovoy.Updater.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// The pre-update safety backup only exists if the delivery service and the DbGateway agree on the route
/// (roadmap Epic 3K). They shipped disagreeing once — <c>/api/backups/run</c> against a gateway that maps
/// <c>/api/backup/run</c> — and nothing failed loudly: the 404 was swallowed as a warning and the update
/// went ahead with no backup. A defect of that class must not be able to come back silently.
/// </summary>
public class UpdaterBackupRouteTests
{
    [Fact]
    public void BackupRequest_TargetsARouteTheGatewayDeclares()
    {
        Assert.Contains(UpdateExecutor.BackupRunPath, DeclaredBackupRoutes());
    }

    [Fact]
    public void BackupRun_AcceptsTheReasonRecordedInTheManifest()
    {
        // Отправляемый службой `?reason=pre-update` должен именно приниматься, а не молча теряться:
        // причина уезжает в манифест бандла и отвечает на вопрос «откуда взялся этот бэкап».
        var endpoint = BackupEndpoints()
            .Single(e => e.RoutePattern.RawText == UpdateExecutor.BackupRunPath);

        var handler = endpoint.Metadata.GetMetadata<System.Reflection.MethodInfo>();

        Assert.NotNull(handler);
        Assert.Contains(handler!.GetParameters(), p => p.Name == "reason");
    }

    private static IReadOnlyList<string?> DeclaredBackupRoutes() =>
        BackupEndpoints().Select(e => e.RoutePattern.RawText).ToList();

    private static IReadOnlyList<RouteEndpoint> BackupEndpoints()
    {
        var builder = WebApplication.CreateBuilder();

        // Handlers are never invoked here — only their routes and signatures are read. The registrations
        // exist because minimal-API metadata inference treats an unregistered parameter as a request body.
        builder.Services.AddSingleton<MongoDB.Driver.IMongoDatabase>(_ => null!);
        builder.Services.AddSingleton<Domovoy.DbGateway.Services.BackupService>(_ => null!);
        builder.Services.AddSingleton<Domovoy.MessageBus.IMessageBus>(_ => null!);

        var app = builder.Build();
        app.MapBackupEndpoints();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }
}

/// <summary>Version ordering used to answer "which of these builds is newer" (roadmap Epic 3K).</summary>
public class SemVerComparerTests
{
    [Theory]
    [InlineData("1.0.11", "1.0.9", true)]     // числовое сравнение, не лексикографическое
    [InlineData("1.2.0", "1.10.0", false)]
    [InlineData("2.0.0", "1.99.99", true)]
    [InlineData("1.0.0", "1.0.0-dev", true)]  // релиз новее пре-релиза той же версии
    [InlineData("1.0.1-dev", "1.0.0", true)]
    [InlineData("1.0.0+abc", "1.0.0+zzz", false)] // метаданные сборки на порядок не влияют
    public void IsNewer_OrdersVersions(string candidate, string current, bool expected) =>
        Assert.Equal(expected, SemVerComparer.IsNewer(candidate, current));

    [Fact]
    public void UnparseableVersion_SortsLowest_AndDoesNotThrow()
    {
        // Кривой тег в реестре не должен ни ронять проверку обновлений, ни оказаться «новейшим».
        Assert.False(SemVerComparer.IsNewer("не-версия", "1.0.0"));
        Assert.True(SemVerComparer.IsNewer("1.0.0", "не-версия"));
    }
}

/// <summary>
/// The v1 trust policy (roadmap Epic 3K): accept only what came from the compiled-in source.
/// Honest scope — this catches accidents and misconfiguration, not a compromised registry; that
/// boundary is the signed-manifest policy this interface is kept ready for.
/// </summary>
public class ReleaseTrustPolicyTests
{
    private static AvailableComponent Candidate(string repository, string digest = "sha256:abc") =>
        new("db-gateway", repository, "dev", digest, "1.0.0", new ComponentDeps { Component = "db-gateway" });

    [Fact]
    public void AcceptsImagesFromTheBuiltInSource()
    {
        var policy = new ConstantSourceTrustPolicy(NullLogger<ConstantSourceTrustPolicy>.Instance);
        var verdict = policy.Verify(new[] { Candidate(ReleaseSource.RepositoryOf("db-gateway")) });

        Assert.True(verdict.Trusted);
    }

    [Fact]
    public void RejectsImagesFromAnywhereElse()
    {
        var policy = new ConstantSourceTrustPolicy(NullLogger<ConstantSourceTrustPolicy>.Instance);
        var verdict = policy.Verify(new[] { Candidate("someone-else/domovoy-db-gateway") });

        Assert.False(verdict.Trusted);
        Assert.Contains("вне встроенного источника", verdict.Reason);
    }

    [Fact]
    public void RejectsCandidatesWithoutADigest()
    {
        // Обновление по подвижному тегу — ровно то, чего адресация по digest и должна не допускать.
        var policy = new ConstantSourceTrustPolicy(NullLogger<ConstantSourceTrustPolicy>.Instance);
        var verdict = policy.Verify(new[] { Candidate(ReleaseSource.RepositoryOf("db-gateway"), digest: "") });

        Assert.False(verdict.Trusted);
    }
}

/// <summary>
/// Topping up the host <c>.env</c> from a release template (roadmap Epic 3K). The invariant under test
/// is the one that makes automatic topology updates safe: the owner's values are never touched.
/// </summary>
public class EnvFileMergerTests
{
    private const string Template = """
        # Канал обновлений
        DOMOVOY_CHANNEL=release

        # ⚠️ Смените перед выносом наружу.
        RABBITMQ_DEFAULT_PASS=change-me

        # Новое в этом релизе
        NEW_SETTING=default-value
        """;

    [Fact]
    public void AddsOnlyMissingKeys_AndNeverRewritesExistingValues()
    {
        var existing = "DOMOVOY_CHANNEL=dev\nRABBITMQ_DEFAULT_PASS=мой-настоящий-пароль\n";

        var result = EnvFileMerger.Merge(existing, Template);

        Assert.Equal(new[] { "NEW_SETTING" }, result.AddedKeys);
        Assert.Contains("RABBITMQ_DEFAULT_PASS=мой-настоящий-пароль", result.Content);
        Assert.Contains("DOMOVOY_CHANNEL=dev", result.Content);      // выбор владельца сохранён
        Assert.DoesNotContain("DOMOVOY_CHANNEL=release", result.Content);
        Assert.Contains("NEW_SETTING=default-value", result.Content);
    }

    [Fact]
    public void CarriesTheCommentAlongWithANewKey()
    {
        // Голое имя переменной владельцу ничего не говорит — пояснение обязано переехать вместе с ней.
        var result = EnvFileMerger.Merge("", Template);

        Assert.Contains("# Новое в этом релизе", result.Content);
        Assert.Contains("NEW_SETTING=default-value", result.Content);
    }

    [Fact]
    public void NothingToAdd_LeavesTheFileByteIdentical()
    {
        var existing = "DOMOVOY_CHANNEL=dev\nRABBITMQ_DEFAULT_PASS=x\nNEW_SETTING=y\n";

        var result = EnvFileMerger.Merge(existing, Template);

        Assert.False(result.Changed);
        Assert.Equal(existing, result.Content);
    }

    [Fact]
    public void SetKey_ReplacesInPlace_AndAppendsWhenAbsent()
    {
        // Так канал, выбранный в интерфейсе, доезжает до .env — иначе ручной `docker compose up -d`
        // молча вернул бы прежний канал.
        var updated = EnvFileMerger.SetKey("A=1\nDOMOVOY_CHANNEL=release\nB=2", "DOMOVOY_CHANNEL", "dev");
        Assert.Contains("DOMOVOY_CHANNEL=dev", updated);
        Assert.Contains("A=1", updated);
        Assert.Contains("B=2", updated);

        var appended = EnvFileMerger.SetKey("A=1", "DOMOVOY_CHANNEL", "dev");
        Assert.Contains("DOMOVOY_CHANNEL=dev", appended);
    }

    [Fact]
    public void GetKey_IgnoresCommentedOutLines()
    {
        Assert.Null(EnvFileMerger.GetKey("# DOMOVOY_CHANNEL=dev", "DOMOVOY_CHANNEL"));
        Assert.Equal("dev", EnvFileMerger.GetKey("DOMOVOY_CHANNEL=dev", "DOMOVOY_CHANNEL"));
    }
}

/// <summary>Parsing the <c>ru.domovoy.deps</c> label an image carries about itself.</summary>
public class ComponentDepsTests
{
    [Fact]
    public void ParsesADeclaredComponent()
    {
        const string label = """
            {"component":"automation-service","container":"automation-service","version":"1.4.57-dev",
             "bus":{"speaks":3,"understands":2},
             "provides":{"automation-api":{"version":3,"minCompat":2}},
             "requires":{"db-api":4}}
            """;

        var deps = ComponentDeps.TryParse(label);

        Assert.NotNull(deps);
        Assert.Equal("automation-service", deps!.Component);
        Assert.Equal(3, deps.Bus!.Speaks);
        Assert.Equal(2, deps.Provides["automation-api"].MinCompat);
        Assert.Equal(4, deps.Requires["db-api"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("не json")]
    [InlineData("{\"version\":\"1.0.0\"}")] // без имени компонента метка бесполезна
    public void MalformedLabel_YieldsNull_RatherThanThrowing(string? label) =>
        Assert.Null(ComponentDeps.TryParse(label));

    [Fact]
    public void NumericVersion_MakesTheWholeLabelUnreadable()
    {
        // Не придирка к типам: ровно так метка бандла топологии и оказалась нечитаемой, а сам бандл —
        // невидимым для решателя. Фиксируем поведение, чтобы «а вдруг число тоже прочитается» больше
        // не было предположением.
        const string label = """{"component":"topology","version":3,"provides":{"topology":{"version":3,"minCompat":1}}}""";

        Assert.Null(ComponentDeps.TryParse(label));
    }
}

/// <summary>
/// Holds the two component lists together (roadmap Epic 3K).
///
/// <para><b>Why this test exists.</b> The system is described in two places on purpose:
/// <c>build/components.json</c> drives what CI publishes, and <c>ReleaseSource.Components</c> drives
/// what the house looks for and in what order it is recreated. They must agree — and when
/// <c>UnifiedDeviceService</c> was retired they briefly did not: the C# list still named a component
/// that no longer had a Dockerfile, a package or a container, so every update check went looking for
/// it in the registry for nothing.</para>
///
/// <para>Nothing else catches this: <c>contract-guard</c> watches <c>components.json</c>, not the
/// constant beside it.</para>
/// </summary>
public class ReleaseSourceTests
{
    [Fact]
    public void ComponentList_MatchesTheBuildSpecification()
    {
        var spec = LoadComponentsSpec();

        var declared = spec
            .RootElement.GetProperty("components")
            .EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var known = ReleaseSource.Components.ToHashSet(StringComparer.Ordinal);

        var missingInCode = declared.Except(known).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var stale = known.Except(declared).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(
            missingInCode.Count == 0 && stale.Count == 0,
            "Списки компонентов разошлись между build/components.json и ReleaseSource.Components.\n" +
            (missingInCode.Count > 0 ? $"  Публикуются, но дом их не ищет: {string.Join(", ", missingInCode)}\n" : "") +
            (stale.Count > 0 ? $"  Дом ищет, но никто не публикует: {string.Join(", ", stale)}\n" : ""));
    }

    [Fact]
    public void RecreationOrder_PutsTheUpdaterLast()
    {
        // Служба обновлений заменяется одноразовым агентом уже после всех остальных: пересоздать
        // себя изнутри контейнер не может, поэтому её место в порядке — не деталь стиля.
        Assert.Equal(ReleaseSource.SelfComponent, ReleaseSource.Components[^1]);

        // Браузер разговаривает с этими двумя, поэтому они идут в конце — иначе интерфейс отвалится
        // на середине обновления, когда до остальных ещё не дошло.
        var order = ReleaseSource.Components.ToList();
        Assert.True(order.IndexOf("api-gateway") > order.IndexOf("db-gateway"));
        Assert.True(order.IndexOf("webui") > order.IndexOf("api-gateway"));
    }

    [Fact]
    public void EveryDeclaredComponentHasABuildableDockerfile()
    {
        // Компонент без Dockerfile — это job сборки, который упадёт в CI. Дешевле поймать здесь.
        var root = RepositoryRoot();
        var spec = LoadComponentsSpec();

        foreach (var component in spec.RootElement.GetProperty("components").EnumerateArray())
        {
            var name = component.GetProperty("name").GetString()!;
            var context = component.GetProperty("context").GetString()!;
            var dockerfile = component.GetProperty("dockerfile").GetString()!;

            var path = context == "."
                ? Path.Combine(root, dockerfile)
                : Path.Combine(root, context, dockerfile);

            Assert.True(File.Exists(path), $"Для компонента '{name}' нет Dockerfile: {path}");
        }
    }

    private static System.Text.Json.JsonDocument LoadComponentsSpec() =>
        System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "build", "components.json")));

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Domovoy.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}

/// <summary>
/// Holds the publishing side and the consuming side of a release together (roadmap Epic 3K).
///
/// <para><b>Why this test exists.</b> <c>build/tools/plan-build.mjs</c> decides what tags and what
/// <c>ru.domovoy.deps</c> labels exist in the channel; <see cref="RegistryClient"/> and
/// <see cref="ComponentDeps"/> decide which of them the house can see. Disagreement between the two is
/// silent by construction — a package that fails either check simply is not in the channel as far as
/// the house is concerned, and the failure surfaces much later as a resolver refusal naming something
/// else entirely.</para>
///
/// <para>That is not hypothetical: the topology bundle shipped as <c>3.4</c> (two numbers, no channel
/// suffix) with <c>"version": 3</c> as a number in its label. Both made it invisible, and the first
/// update attempt on a fresh installation refused with "connectivity-service requires the interface
/// 'topology', which nobody provides" — while the bundle sat in the registry.</para>
/// </summary>
public class ReleaseTagFormatTests
{
    [Theory]
    [InlineData("dev")]
    [InlineData("release")]
    public void EveryPublishedTagIsVisibleToTheHouse_AndEveryLabelParses(string channel)
    {
        var plan = RunPlanner(channel);
        if (plan is null) return; // node недоступен — проверять нечего, см. RunPlanner

        using (plan)
        {
            var packages = plan.RootElement
                .GetProperty("matrix").GetProperty("include")
                .EnumerateArray()
                .Select(c => (Name: c.GetProperty("name").GetString()!,
                              Version: c.GetProperty("version").GetString()!,
                              Deps: c.GetProperty("deps").GetString()!))
                .ToList();

            var topology = plan.RootElement.GetProperty("topology");
            Assert.Equal(System.Text.Json.JsonValueKind.Object, topology.ValueKind);

            packages.Add((
                "topology",
                topology.GetProperty("version").GetString()!,
                topology.GetProperty("deps").GetString()!));

            foreach (var (name, version, depsLabel) in packages)
            {
                Assert.True(
                    RegistryClient.IsChannelVersionTag(version, channel),
                    $"Тег '{version}' компонента '{name}' не проходит отбор версий канала '{channel}' — " +
                    "для дома этот пакет в канале не существует.");

                var deps = ComponentDeps.TryParse(depsLabel);
                Assert.True(deps is not null, $"Метка ru.domovoy.deps компонента '{name}' не разбирается: {depsLabel}");

                Assert.Equal(name, deps!.Component);
                Assert.Equal(version, deps.Version);
            }
        }
    }

    [Fact]
    public void TopologyBundleDeclaresTheInterfaceComponentsDependOn()
    {
        // Бандл — единственный провайдер интерфейса `topology`. Потеря этого объявления мгновенно
        // превращается в отказ обновления у всех, кто требует топологию.
        var plan = RunPlanner("dev");
        if (plan is null) return;

        using (plan)
        {
            var deps = ComponentDeps.TryParse(
                plan.RootElement.GetProperty("topology").GetProperty("deps").GetString());

            Assert.NotNull(deps);
            Assert.True(deps!.Provides.ContainsKey("topology"));
            Assert.True(deps.Provides["topology"].MinCompat <= deps.Provides["topology"].Version);
        }
    }

    /// <summary>
    /// Runs the real build planner over the real specification. <c>--all</c> keeps it offline: the
    /// "top up what is missing in the channel" probe only runs for a diff-driven plan.
    /// <para>Returns null when node is absent, so a machine without it does not fail the suite — CI has
    /// node (the WebUI needs it), which is where this check has to hold.</para>
    /// </summary>
    private static System.Text.Json.JsonDocument? RunPlanner(string channel)
    {
        var start = new System.Diagnostics.ProcessStartInfo("node")
        {
            WorkingDirectory = ReleaseSourceTests.RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in new[]
                 {
                     "build/tools/plan-build.mjs", "--all",
                     "--channel", channel, "--run", "57", "--sha", "abc1234def",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        System.Diagnostics.Process? process;
        try
        {
            process = System.Diagnostics.Process.Start(start);
        }
        catch (Exception)
        {
            return null;
        }

        if (process is null) return null;

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, $"plan-build.mjs завершился с кодом {process.ExitCode}: {stderr}");
            return System.Text.Json.JsonDocument.Parse(stdout);
        }
    }
}
