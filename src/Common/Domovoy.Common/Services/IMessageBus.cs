// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.MessageBus;

public interface IMessageBus : IDisposable
{
    Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default);
    Task SubscribeAsync<T>(string queue, string exchange, string routingKey, Func<T, Task> handler, CancellationToken cancellationToken = default);
    Task UnsubscribeAsync(string queue, CancellationToken cancellationToken = default);
}
