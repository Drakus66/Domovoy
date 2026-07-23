// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// Live copy of the intelligence-layer switches (roadmap Epic 3I). <see cref="Services.RefreshLoop"/> re-reads
/// the <c>ml_settings</c> document each cycle and pushes it here, so toggling the layer or the proposers from the
/// UI takes effect within one refresh without a restart. Every ML contour reads this singleton: the trainer and
/// model-service honour <see cref="LayerEnabled"/>; the proposers honour <see cref="ProposalsEnabled"/> plus the
/// cold-start gate (<see cref="MinHistoryDays"/>). Defaults to fully enabled, so the layer works out of the box
/// and a gateway outage never silently disables it (offline-first: keep the last-known settings).
/// </summary>
public sealed class MlRuntimeState
{
    private volatile MlSettings _settings = new();

    /// <summary>The last-known settings (never null).</summary>
    public MlSettings Current => _settings;

    /// <summary>Master switch — training, model serving and every proposer are off when false.</summary>
    public bool LayerEnabled => _settings.Enabled;

    /// <summary>Whether the periodic proposers may queue proposals — requires the layer on AND proposals on.</summary>
    public bool ProposalsEnabled => _settings.Enabled && _settings.ProposalsEnabled;

    /// <summary>Cold-start gate: proposers stay silent until history is at least this many days old (0 disables).</summary>
    public int MinHistoryDays => Math.Max(0, _settings.MinHistoryDays);

    /// <summary>Replace the live settings (called by the refresh loop). A null is ignored — keep last-known.</summary>
    public void Set(MlSettings? settings)
    {
        if (settings is not null) _settings = settings;
    }
}
