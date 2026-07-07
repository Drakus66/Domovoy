// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// The current home mode / presence context (roadmap Epic 1G), stored as a single document in the
/// <c>home_state</c> collection (fixed id <see cref="SingletonId"/>). The DbGateway is the persistence
/// authority: a manual switch (UI → ApiGateway proxy) or a presence-driven switch (AutomationService)
/// both go through <c>PUT /api/mode</c>, which upserts this document, publishes
/// <see cref="Domovoy.Contracts.Messaging.HomeModeChangedV1"/> on the bus and records the change in the
/// P0-5 event-log. Everyone else learns the mode by consuming that event.
/// </summary>
public class HomeState
{
    /// <summary>There is only ever one home-state document; this is its stable id.</summary>
    public const string SingletonId = "current";

    [BsonId]
    public string Id { get; set; } = SingletonId;

    /// <summary>Current mode (open set; see <see cref="Domovoy.Contracts.Home.WellKnownModes"/>).</summary>
    public string Mode { get; set; } = Domovoy.Contracts.Home.WellKnownModes.Default;

    /// <summary>Who set it: <c>user</c> | <c>presence</c> | <c>rule</c> | <c>ml</c> (open set).</summary>
    public string Source { get; set; } = Domovoy.Contracts.Home.ModeChangeSources.User;

    /// <summary>When the mode was last changed (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
