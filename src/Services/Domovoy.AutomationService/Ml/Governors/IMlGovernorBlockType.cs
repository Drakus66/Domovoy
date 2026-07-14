// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Ml.Governors;

/// <summary>
/// Marks a block type as an ML governor and names the ML target capability it consumes (Epic 2P). The block
/// catalog surfaces this in the catalog DTO so the UI can join an ML task to its consumer block types/instances
/// ("task → consumers") and a device to the models applicable to it ("device → apply model") without
/// port-name heuristics. The target equals the governor's measured input — the model predicts the signal the
/// governor monitors.
/// </summary>
public interface IMlGovernorBlockType
{
    /// <summary>The capability whose models this governor type serves (matches <see cref="Domovoy.Contracts.Ml.MlTask.TargetCapability"/>).</summary>
    string MlTargetCapability { get; }
}
