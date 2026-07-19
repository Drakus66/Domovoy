// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.IdentityModel.Tokens.Jwt;

using Domovoy.ApiGateway.Services;
using Domovoy.Contracts.Security;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline unit tests for the JWT mint/validate logic (mobile-app / remote-access track). Asserts the access
/// token carries the resolved permissions as claims, that the refresh token round-trips to its subject id, and —
/// the security-critical property — that an access token cannot be replayed where a refresh token is expected
/// (distinct audience) and that a token signed by a different key is rejected.
/// </summary>
public sealed class JwtTokenServiceTests
{
    private static JwtTokenService Service(string secret = "unit-test-secret-key-that-is-plenty-long-enough-1234567890") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = secret,
            ["JwtSettings:Issuer"] = "domovoy",
            ["JwtSettings:Audience"] = "domovoy-clients",
            ["JwtSettings:ExpiryMinutes"] = "60",
            ["JwtSettings:RefreshExpiryDays"] = "30",
        }).Build());

    private static AuthUserInfo SampleUser() => new(
        "user-123", "alice", "Alice", "alice@home.local",
        new List<string> { "resident" },
        new List<string> { WellKnownPermissions.DevicesView, WellKnownPermissions.DevicesControl });

    [Fact]
    public void AccessToken_CarriesIdentityAndPermissionClaims()
    {
        var (token, expiresAt) = Service().CreateAccessToken(SampleUser());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("user-123", jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("alice", jwt.Claims.Single(c => c.Type == JwtTokenService.NameClaim).Value);
        Assert.Contains(jwt.Claims, c => c.Type == JwtTokenService.RoleClaim && c.Value == "resident");

        var perms = jwt.Claims.Where(c => c.Type == AuthConstants.PermissionClaim).Select(c => c.Value).ToList();
        Assert.Contains(WellKnownPermissions.DevicesView, perms);
        Assert.Contains(WellKnownPermissions.DevicesControl, perms);
        Assert.True(expiresAt > DateTime.UtcNow);
    }

    [Fact]
    public void RefreshToken_ValidatesBackToSubjectId()
    {
        var svc = Service();
        var refresh = svc.CreateRefreshToken("user-123");
        Assert.Equal("user-123", svc.ValidateRefreshToken(refresh));
    }

    [Fact]
    public void AccessToken_CannotBeUsedAsRefreshToken()
    {
        var svc = Service();
        var (access, _) = svc.CreateAccessToken(SampleUser());

        // Different audience → the refresh validation must reject the access token.
        Assert.Null(svc.ValidateRefreshToken(access));
    }

    [Fact]
    public void RefreshToken_SignedByAnotherKey_IsRejected()
    {
        var mint = Service("secret-key-number-one-that-is-plenty-long-enough-ok-1234");
        var other = Service("secret-key-number-two-completely-different-but-long-5678");

        var refresh = mint.CreateRefreshToken("user-123");
        Assert.Null(other.ValidateRefreshToken(refresh));
    }

    [Fact]
    public void ValidateRefreshToken_Garbage_ReturnsNull()
    {
        var svc = Service();
        Assert.Null(svc.ValidateRefreshToken("not-a-jwt"));
        Assert.Null(svc.ValidateRefreshToken(""));
    }
}
