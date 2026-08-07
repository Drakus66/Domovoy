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
}
