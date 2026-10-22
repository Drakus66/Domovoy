// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Ml.Templates;
using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Capabilities;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Descriptor-based capability typing for the ML trainer (roadmap Epic 2I). The kind decides which template
/// family may model a target, so the enum branch — the whole multiclass cell and the <c>ml_selector</c> governor
/// — is reachable only if an enum target is actually typed as one. Pure/offline.
/// </summary>
public sealed class CapabilityKindResolverTests
{
    private static DbGatewayClient.DeviceSnapshot Device(params (string Id, string Kind)[] capabilities) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Capabilities = capabilities
            .Select(c => new DbGatewayClient.CapabilitySnapshot { Id = c.Id, Kind = c.Kind })
            .ToList(),
    };

    [Fact]
    public void ResolvesEnumTarget_FromTheLiveDescriptor()
    {
        var resolver = new CapabilityKindResolver();
        resolver.Sync(new[] { Device(("hvac_mode", "Enum"), ("temperature", "Number")) });

        // Before this was descriptor-based, hvac_mode came back as Number and trained as a regression: the
        // multiclass template (Target = Enum) could never be selected for it.
        Assert.Equal(CapabilityKind.Enum, resolver.KindOf("hvac_mode"));
        Assert.Equal(CapabilityKind.Number, resolver.KindOf("temperature"));
    }

    [Fact]
    public void ResolvesCustomPluginCapability_FromTheLiveDescriptor()
    {
        var resolver = new CapabilityKindResolver();
        resolver.Sync(new[] { Device(("acme:fan_preset", "Enum"), ("acme:leak", "Boolean")) });

        Assert.Equal(CapabilityKind.Enum, resolver.KindOf("acme:fan_preset"));
        Assert.Equal(CapabilityKind.Boolean, resolver.KindOf("acme:leak"));
    }

    [Fact]
    public void FallsBackToWellKnown_WhenTheReadModelHasNothing()
    {
        var resolver = new CapabilityKindResolver(); // never synced: cold start / gateway unreachable

        Assert.Equal(CapabilityKind.Boolean, resolver.KindOf(CapabilityIds.OnOff));
        Assert.Equal(CapabilityKind.Enum, resolver.KindOf(CapabilityIds.HvacMode));
        Assert.Equal(CapabilityKind.Enum, resolver.KindOf(CapabilityIds.PowerSource));
        Assert.Equal(CapabilityKind.Text, resolver.KindOf(CapabilityIds.Clock));
        Assert.Equal(CapabilityKind.Number, resolver.KindOf(CapabilityIds.Temperature));
        Assert.Equal(CapabilityKind.Number, resolver.KindOf("totally:unknown")); // the common ML target
    }

    [Fact]
    public void KeepsLastGoodMap_WhenARefreshBringsNothing()
    {
        var resolver = new CapabilityKindResolver();
        resolver.Sync(new[] { Device(("hvac_mode", "Enum")) });

        resolver.Sync(Array.Empty<DbGatewayClient.DeviceSnapshot>());

        Assert.Equal(CapabilityKind.Enum, resolver.KindOf("hvac_mode")); // offline-first, like ZoneCache
    }

    [Fact]
    public void ResolvesDisagreementByMajority_Deterministically()
    {
        var resolver = new CapabilityKindResolver();
        resolver.Sync(new[]
        {
            Device(("level", "Number")),
            Device(("level", "Number")),
            Device(("level", "Enum")),
        });

        Assert.Equal(CapabilityKind.Number, resolver.KindOf("level"));
    }

    [Fact]
    public void IgnoresUnparseableKind()
    {
        var resolver = new CapabilityKindResolver();
        resolver.Sync(new[] { Device((CapabilityIds.OnOff, "nonsense")) });

        Assert.Equal(CapabilityKind.Boolean, resolver.KindOf(CapabilityIds.OnOff)); // well-known wins over garbage
    }
}
