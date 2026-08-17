// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Ml.Templates;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Model selection + zone auto-promotion under an <b>unevaluated</b> holdout (roadmap Epic 2I). A template that
/// could not hold out a slice honestly reports null, not 0 — for a lower-is-better metric a zero would read as a
/// flawless model, so it would win selection against a measured candidate and clear the promotion margin against
/// any real fallback, splintering a zone onto its own model on nothing but thin data. Pure/offline.
/// </summary>
public sealed class ModelSelectionGateTests
{
    private sealed class Stub : IModelTemplate
    {
        public Stub(string metric, bool lowerIsBetter)
        {
            Metric = metric;
            LowerIsBetter = lowerIsBetter;
        }

        public string Kind => "stub";
        public CapabilityKind Target => CapabilityKind.Number;
        public string Metric { get; }
        public bool LowerIsBetter { get; }
        public string Algorithm => "stub";

        public TemplateResult? Train(IReadOnlyList<LabeledSample> samples, int minSamples) => null;
    }

    private static readonly Stub Mae = new("MAE", lowerIsBetter: true);
    private static readonly Stub Auc = new("AUC", lowerIsBetter: false);

    [Fact]
    public void EvaluatedCandidate_BeatsUnevaluatedIncumbent()
    {
        Assert.True(ModelTemplateRegistry.IsBetter(Mae, candidate: 1.4, incumbent: null));
        Assert.True(ModelTemplateRegistry.IsBetter(Auc, candidate: 0.55, incumbent: null));
    }

    [Fact]
    public void UnevaluatedCandidate_NeverWinsSelection()
    {
        // The regression case that used to break: null-as-0 would have beaten a measured MAE of 1.4.
        Assert.False(ModelTemplateRegistry.IsBetter(Mae, candidate: null, incumbent: 1.4));
        Assert.False(ModelTemplateRegistry.IsBetter(Auc, candidate: null, incumbent: 0.55));
        Assert.False(ModelTemplateRegistry.IsBetter(Mae, candidate: null, incumbent: null));
    }

    [Fact]
    public void MeasuredScores_StillCompareByOrientation()
    {
        Assert.True(ModelTemplateRegistry.IsBetter(Mae, candidate: 0.9, incumbent: 1.4));
        Assert.False(ModelTemplateRegistry.IsBetter(Mae, candidate: 1.9, incumbent: 1.4));
        Assert.True(ModelTemplateRegistry.IsBetter(Auc, candidate: 0.8, incumbent: 0.55));
        Assert.False(ModelTemplateRegistry.IsBetter(Auc, candidate: 0.4, incumbent: 0.55));
    }

    [Fact]
    public void Zone_IsNotPromoted_WhenEitherSideIsUnevaluated()
    {
        // Candidate unevaluated: no evidence it beats the shared model → stay on the fallback.
        Assert.False(ModelTemplateRegistry.ShouldPromote(Mae, candidate: null, fallback: 2.0, margin: 0.25));
        // Fallback unevaluated: nothing to compare against → same answer.
        Assert.False(ModelTemplateRegistry.ShouldPromote(Mae, candidate: 0.5, fallback: null, margin: 0.25));
        Assert.False(ModelTemplateRegistry.ShouldPromote(Auc, candidate: null, fallback: 0.6, margin: 0.25));
    }

    [Fact]
    public void Zone_IsPromoted_OnlyBeyondTheMargin()
    {
        Assert.True(ModelTemplateRegistry.ShouldPromote(Mae, candidate: 1.0, fallback: 2.0, margin: 0.25));
        Assert.False(ModelTemplateRegistry.ShouldPromote(Mae, candidate: 1.9, fallback: 2.0, margin: 0.25)); // within noise
        Assert.True(ModelTemplateRegistry.ShouldPromote(Auc, candidate: 0.9, fallback: 0.6, margin: 0.25));
        Assert.False(ModelTemplateRegistry.ShouldPromote(Auc, candidate: 0.7, fallback: 0.6, margin: 0.25));
    }

    [Fact]
    public void AmbientMode_IsAdmittedForEveryScope_AndFlowsThroughTheLocalityPolicy()
    {
        // The feature-locality policy now sits in the training path (ContextFeatureJoin.WithAdmissibleMode).
        // Home mode is ambient, so every scope gets it — including a global model, which admits ambient only.
        var start = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 4).Select(i => new LabeledSample(start.AddHours(i), 21.0)).ToList();
        var timeline = new[] { (start.AddMinutes(-1), "Night") };
        string? KindOf(string? zoneId) => zoneId == "z1" ? "room" : null;

        foreach (var scope in new[] { ModelScope.Global, ModelScope.ZoneKind("room"), ModelScope.Zone("z1") })
        {
            var joined = ContextFeatureJoin.WithAdmissibleMode(rows, timeline, scope, KindOf);
            Assert.All(joined, s => Assert.Equal("Night", s.Mode));
        }
    }
}
