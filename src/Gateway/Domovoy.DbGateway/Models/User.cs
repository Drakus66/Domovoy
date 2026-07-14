// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = null!;

    public string Username { get; set; } = null!;
    
    public string PasswordHash { get; set; } = null!;
    
    public string Email { get; set; } = null!;
    
    public string Role { get; set; } = "User";
    
    public Dictionary<string, object>? Preferences { get; set; }
}
