// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Models;

namespace Domovoy.DbGateway.Stores;

/// <summary>
/// Domain store for update settings (roadmap Epic 3K), written against the storage-abstraction seam
/// introduced in Epic 3H. Expressed in domain operations only — no <c>IMongoDatabase</c>,
/// <c>FilterDefinition</c> or <c>BsonDocument</c> crosses this boundary — so a second backend
/// (PostgreSQL, 3H Ф2) is a new implementation of this contract and nothing above it changes.
/// See <c>docs/architecture/db_abstraction_ru.md</c>.
/// </summary>
public interface IUpdateStore
{
    /// <summary>Current settings, or freshly defaulted ones on an installation that never saved any.</summary>
    Task<UpdateSettings> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>Upserts the settings singleton.</summary>
    Task SaveSettingsAsync(UpdateSettings settings, CancellationToken ct = default);

    /// <summary>Records the outcome of a registry check without touching the operator's choices.</summary>
    Task RecordCheckAsync(DateTime at, string result, CancellationToken ct = default);
}
