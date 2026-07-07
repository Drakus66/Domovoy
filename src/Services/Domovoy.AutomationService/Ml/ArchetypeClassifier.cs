// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;

using Microsoft.ML;
using Microsoft.ML.Data;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// ML.NET multiclass classifier for device archetypes (roadmap Epic 2D — the empirical upgrade over the
/// deterministic heuristic <c>DeviceClassifier</c>). It learns the mapping from a device's capability
/// signature (a multi-hot vector over the well-known capabilities + a "writable on/off" flag) to its
/// archetype, trained on the labelled device population — where user overrides are ground truth that
/// <i>corrects</i> the heuristic, so the model can generalize a correction to similar unseen signatures.
/// Pure and dependency-free (unit-tested without infrastructure); SDCA maximum-entropy, no extra package.
/// </summary>
public sealed class ArchetypeClassifier
{
    /// <summary>A labelled training/inference example: the device's capability signature + its archetype.</summary>
    public sealed record Example(
        IReadOnlyCollection<string> Capabilities,
        IReadOnlyCollection<string> WritableCapabilities,
        string Archetype);

    public sealed record Prediction(string Archetype, float Confidence);

    // Fixed capability vocabulary → the multi-hot feature layout. Order is stable so a saved model would line
    // up (v1 keeps the engine in-memory). FeatureCount = Vocab.Length + 1 (the writable-on_off flag).
    private static readonly string[] Vocab =
    {
        CapabilityIds.OnOff, CapabilityIds.Brightness, CapabilityIds.Color, CapabilityIds.ColorTemp,
        CapabilityIds.Temperature, CapabilityIds.TemperatureSetpoint, CapabilityIds.Humidity, CapabilityIds.Co2,
        CapabilityIds.Occupancy, CapabilityIds.Contact, CapabilityIds.Lock, CapabilityIds.Valve,
        CapabilityIds.Power, CapabilityIds.Energy, CapabilityIds.Battery, CapabilityIds.Illuminance,
        CapabilityIds.Position, CapabilityIds.HvacMode, CapabilityIds.FanSpeed,
    };
    private const int FeatureCount = 20; // Vocab.Length (19) + writable-on_off flag; must match VectorType below

    private PredictionEngine<Row, RowPrediction>? _engine;

    public bool IsTrained => _engine is not null;

    /// <summary>
    /// Fit the classifier on the labelled examples. Needs ≥2 distinct archetypes and a couple of examples per
    /// class to be meaningful; returns the number of examples trained on, or 0 (and stays untrained) if there
    /// is too little signal.
    /// </summary>
    public int Train(IReadOnlyList<Example> examples)
    {
        var distinctClasses = examples.Select(e => e.Archetype).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (examples.Count < 4 || distinctClasses < 2)
        {
            _engine = null;
            return 0;
        }

        var ml = new MLContext(seed: 0);
        var data = ml.Data.LoadFromEnumerable(examples.Select(ToRow).ToList());

        var pipeline = ml.Transforms.Conversion.MapValueToKey("LabelKey", nameof(Row.Label))
            .Append(ml.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey", "Features"))
            .Append(ml.Transforms.Conversion.MapKeyToValue("PredictedLabel", "PredictedLabel"));

        var model = pipeline.Fit(data);
        _engine = ml.Model.CreatePredictionEngine<Row, RowPrediction>(model);
        return examples.Count;
    }

    /// <summary>Predict a device's archetype + confidence (max class probability), or null if untrained.</summary>
    public Prediction? Predict(IReadOnlyCollection<string> capabilities, IReadOnlyCollection<string> writableCapabilities)
    {
        if (_engine is null) return null;
        var p = _engine.Predict(ToRow(new Example(capabilities, writableCapabilities, string.Empty)));
        var confidence = p.Score is { Length: > 0 } ? p.Score.Max() : 0f;
        return new Prediction(p.PredictedLabel ?? DeviceArchetypeUnknown, confidence);
    }

    private const string DeviceArchetypeUnknown = "unknown";

    private static Row ToRow(Example e)
    {
        var present = new HashSet<string>(e.Capabilities, StringComparer.OrdinalIgnoreCase);
        var features = new float[FeatureCount];
        for (var i = 0; i < Vocab.Length; i++)
            features[i] = present.Contains(Vocab[i]) ? 1f : 0f;
        features[Vocab.Length] =
            e.WritableCapabilities.Any(w => string.Equals(w, CapabilityIds.OnOff, StringComparison.OrdinalIgnoreCase)) ? 1f : 0f;

        return new Row { Features = features, Label = e.Archetype };
    }

    private sealed class Row
    {
        [VectorType(FeatureCount)]
        public float[] Features { get; set; } = new float[FeatureCount];

        public string Label { get; set; } = string.Empty;
    }

    private sealed class RowPrediction
    {
        public string? PredictedLabel { get; set; }
        public float[] Score { get; set; } = Array.Empty<float>();
    }
}
