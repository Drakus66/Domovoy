// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.SignalR;

namespace Domovoy.ApiGateway.Hubs;

/// <summary>
/// Dedicated SignalR hub for user-facing notification banners (2M.2). Kept separate from <see cref="DeviceHub"/>
/// on purpose: DeviceHub fans the whole device-state firehose out to every connection, so a client that only
/// wants notifications would receive — and log a "no client method" warning for — every single state update.
/// Clients connect here for banners only; the server pushes <c>NotificationRaised</c> and nothing else, so this
/// connection stays quiet. Receive-only for clients (no invokable server methods).
/// </summary>
public sealed class NotificationHub : Hub
{
}
