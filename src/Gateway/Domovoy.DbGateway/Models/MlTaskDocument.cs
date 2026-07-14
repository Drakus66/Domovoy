// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Ml;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// Persisted ML training task (Epic 2P) in the <c>ml_tasks</c> collection: the <see cref="MlTask"/> contract
/// plus a normalized target key that backs the case-insensitive uniqueness of one-task-per-target.
/// </summary>
public class MlTaskDocument : MlTask
{
    /// <summary>Lower-case <see cref="MlTask.TargetCapability"/> — unique-index key (server-maintained).</summary>
    public string TargetKey { get; set; } = string.Empty;
}
