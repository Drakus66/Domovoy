// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Devices;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// Advisory runner for the ML.NET archetype classifier (roadmap Epic 2D). Trains an <see cref="ArchetypeClassifier"/>
/// on the current device population — labelled by each device's effective archetype (user override wins over the
/// heuristic auto-label) — then re-classifies every device and surfaces <b>disagreements</b>: devices the model
/// would type differently than they currently are. Those are review candidates (a mis-heuristic'd device, or one
/// worth a manual override). Advisory only: it never mutates the read-model, mirroring 1F's "propose, don't act".
/// </summary>
public sealed class ArchetypeAdvisor
{
    private readonly DbGatewayClient _db;
    private readonly ILogger<ArchetypeAdvisor> _logger;

    public ArchetypeAdvisor(DbGatewayClient db, ILogger<ArchetypeAdvisor> logger)
    {
        _db = db;
        _logger = logger;
    }

    public sealed record Disagreement(string DeviceId, string Name, string Current, string Predicted, double Confidence);
    public sealed record Result(bool Trained, int Devices, int TrainedOn, IReadOnlyList<Disagreement> Disagreements, string Note);

    /// <summary>Minimum confidence before the model's disagreement is worth surfacing.</summary>
    private const double MinConfidence = 0.6;

    public async Task<Result> RunAsync(CancellationToken ct)
    {
        var devices = await _db.GetDevicesAsync(ct) ?? new List<DbGatewayClient.DeviceSnapshot>();

        // Train on devices the heuristic (or a human) already typed with confidence — skip Unknown labels (noise).
        var labelled = devices
            .Where(d => d.Capabilities.Count > 0 && !string.Equals(d.EffectiveArchetype, DeviceArchetypes.Unknown, StringComparison.OrdinalIgnoreCase))
            .Select(ToExample)
            .ToList();

        var classifier = new ArchetypeClassifier();
        var trainedOn = classifier.Train(labelled);
        if (trainedOn == 0)
            return new Result(false, devices.Count, 0, Array.Empty<Disagreement>(),
                "not enough labelled devices across ≥2 archetypes to train");

        var disagreements = new List<Disagreement>();
        foreach (var d in devices.Where(d => d.Capabilities.Count > 0))
        {
            var prediction = classifier.Predict(
                d.Capabilities.Select(c => c.Id).ToList(),
                d.Capabilities.Where(c => c.Writable).Select(c => c.Id).ToList());
            if (prediction is null) continue;

            if (!string.Equals(prediction.Archetype, d.EffectiveArchetype, StringComparison.OrdinalIgnoreCase)
                && prediction.Confidence >= MinConfidence)
            {
                disagreements.Add(new Disagreement(
                    d.Id, d.Name, d.EffectiveArchetype, prediction.Archetype, Math.Round(prediction.Confidence, 3)));
            }
        }

        _logger.LogInformation("Archetype classifier trained on {N} devices; {D} disagreement(s)", trainedOn, disagreements.Count);
        return new Result(true, devices.Count, trainedOn, disagreements,
            disagreements.Count == 0 ? "model agrees with the current archetypes" : "review candidates found");
    }

    private static ArchetypeClassifier.Example ToExample(DbGatewayClient.DeviceSnapshot d) => new(
        d.Capabilities.Select(c => c.Id).ToList(),
        d.Capabilities.Where(c => c.Writable).Select(c => c.Id).ToList(),
        d.EffectiveArchetype);
}
