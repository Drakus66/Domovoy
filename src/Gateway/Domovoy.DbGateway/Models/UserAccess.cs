// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class UserAccess
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string AccessId { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = null!;
    
    [BsonRepresentation(BsonType.ObjectId)]
    public string DeviceId { get; set; } = null!;
    
    public string Permission { get; set; } = "Read"; // Read, Write, Admin
    
    [BsonRepresentation(BsonType.ObjectId)]
    public string GrantedBy { get; set; } = null!;
    
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}
