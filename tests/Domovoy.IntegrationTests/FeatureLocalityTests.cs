// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Ml.Templates;
using Domovoy.Contracts.Ml;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the spatial feature-locality policy (roadmap Epic 2I, Phase 4): the same zone chain that
/// scopes a model scopes its inputs, with a hard wall at the zone-kind boundary. Mirrors the design examples —
/// the living room nudges the bedroom (soft), the house never feeds the greenhouse (hard).
/// </summary>
public sealed class FeatureLocalityTests
{
    // bedroom & living are "room"; greenhouse is "greenhouse".
    private static string? KindOf(string? zoneId) => zoneId switch
    {
        "bedroom" or "living" => "room",
        "greenhouse" => "greenhouse",
        _ => null,
    };

    private static FeatureAdmission Eval(ModelScope scope, FeatureCandidate c) =>
        FeatureLocality.Evaluate(scope, c, KindOf);

    [Fact]
    public void Ambient_FeedsEveryModel_AtFullWeight()
    {
        var a = Eval(ModelScope.Zone("greenhouse"), new FeatureCandidate("home_mode", Ambient: true));
        Assert.True(a.Admissible);
        Assert.Equal(1.0, a.Weight);
    }

    [Fact]
    public void OwnZoneSensor_FullWeight()
    {
        var a = Eval(ModelScope.Zone("bedroom"), new FeatureCandidate("temperature", "bedroom"));
        Assert.True(a.Admissible);
        Assert.Equal(1.0, a.Weight);
    }

    [Fact]
    public void SameKindSiblingZone_Admissible_ButDownWeighted()
    {
        // Living room → bedroom model: same kind "room", different zone → soft, reduced weight.
        var a = Eval(ModelScope.Zone("bedroom"), new FeatureCandidate("temperature", "living"));
        Assert.True(a.Admissible);
        Assert.True(a.Weight is > 0 and < 1, $"sibling weight should be reduced, was {a.Weight}");
    }

    [Fact]
    public void AcrossZoneKindBoundary_HardExcluded()
    {
        // House sensor → greenhouse model: different kind → excluded a priori (the hard wall).
        Assert.False(Eval(ModelScope.Zone("greenhouse"), new FeatureCandidate("temperature", "living")).Admissible);
        // …and the reverse: greenhouse sensor → bedroom model.
        Assert.False(Eval(ModelScope.Zone("bedroom"), new FeatureCandidate("soil_moisture", "greenhouse")).Admissible);
    }

    [Fact]
    public void ZoneKindModel_AdmitsSameKind_ExcludesOthers()
    {
        var roomModel = ModelScope.ZoneKind("room");
        Assert.True(Eval(roomModel, new FeatureCandidate("temperature", "living")).Admissible);
        Assert.False(Eval(roomModel, new FeatureCandidate("temperature", "greenhouse")).Admissible);
    }

    [Fact]
    public void GlobalModel_UsesAmbientOnly()
    {
        Assert.True(Eval(ModelScope.Global, new FeatureCandidate("outdoor_temp", Ambient: true)).Admissible);
        Assert.False(Eval(ModelScope.Global, new FeatureCandidate("temperature", "bedroom")).Admissible);
    }

    [Fact]
    public void Admissible_FiltersAcrossTheKindBoundary()
    {
        var candidates = new[]
        {
            new FeatureCandidate("home_mode", Ambient: true),
            new FeatureCandidate("temperature", "bedroom"),
            new FeatureCandidate("temperature", "living"),
            new FeatureCandidate("soil_moisture", "greenhouse"),
        };

        var kept = FeatureLocality.Admissible(ModelScope.Zone("bedroom"), candidates, KindOf);

        Assert.Equal(3, kept.Count); // ambient + own + sibling; greenhouse excluded
        Assert.DoesNotContain(kept, k => k.Feature.ZoneId == "greenhouse");
    }
}
