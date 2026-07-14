// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// Number → regression template (roadmap Epic 2I) — the flagship cell of the template grid: a learned
/// schedule (time-of-day/day-of-week → numeric value), the basis of the ML-thermostat setpoint. Wraps the
/// existing <see cref="MlTrainer"/> (ML.NET SDCA + chronological-holdout MAE) so the registry can select it
/// against future Number templates by holdout MAE.
/// </summary>
public sealed class ScheduleRegressionTemplate : IModelTemplate
{
    private readonly MlTrainer _trainer = new();

    public string Kind => MlModelKinds.ScheduleRegression;
    public CapabilityKind Target => CapabilityKind.Number;
    public string Metric => "MAE";
    public bool LowerIsBetter => true;
    public string Algorithm => MlTrainer.Algorithm;

    public TemplateResult? Train(IReadOnlyList<LabeledSample> samples, int minSamples)
    {
        var rows = samples.Select(s => (s.Timestamp, s.Value)).ToList();
        var r = _trainer.Train(rows, minSamples);
        return r is null
            ? null
            : new TemplateResult(r.Artifact, r.Rmse, r.SampleCount, r.HoldoutMae, r.HoldoutCount);
    }
}
