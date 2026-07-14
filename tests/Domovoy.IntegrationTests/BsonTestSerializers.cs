// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Serializers;

using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Mirrors DbGateway Program.cs BSON serializer registration for tests. Registration is global and
/// once-per-process, so every test that needs it funnels through this single guard — two independent
/// guards (e.g. InfraFixture and a pure round-trip test) would throw on the second registration.
/// </summary>
internal static class BsonTestSerializers
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0) return;

        BsonSerializer.RegisterSerializer(new ObjectSerializer());
        BsonSerializer.RegisterSerializer(new JsonElementSerializer());
        BsonSerializer.RegisterSerializer(new JsonObjectDictionarySerializer());
    }
}
