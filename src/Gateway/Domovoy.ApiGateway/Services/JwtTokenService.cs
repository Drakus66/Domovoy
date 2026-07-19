// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using Domovoy.Contracts.Security;
using Microsoft.IdentityModel.Tokens;

namespace Domovoy.ApiGateway.Services;

/// <summary>
/// Mints and validates the JWT pair for local auth (mobile-app / remote-access track). A short-lived <b>access
/// token</b> carries the user's identity + permissions as claims (so authorization is stateless per request); a
/// long-lived <b>refresh token</b> (distinct audience, so it can't be replayed as an access token) carries only
/// the subject id, and refresh re-resolves the user against the DbGateway to pick up permission/enabled changes.
///
/// <para>Inbound claim mapping is turned off everywhere (short claim names survive verbatim), so the same claim
/// names used here — <c>sub</c>, <c>uname</c>, <c>role</c>, <see cref="AuthConstants.PermissionClaim"/> — are what
/// the JwtBearer validation and the permission policies read back.</para>
/// </summary>
public sealed class JwtTokenService
{
    public const string DisplayNameClaim = "display_name";

    private readonly SymmetricSecurityKey _key;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessMinutes;
    private readonly int _refreshDays;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public JwtTokenService(IConfiguration config)
    {
        var s = config.GetSection("JwtSettings");
        var secret = s["SecretKey"] ?? "DefaultDevelopmentSecretKeyThatShouldBeReplacedInProduction";
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _issuer = s["Issuer"] ?? "domovoy";
        _audience = s["Audience"] ?? "domovoy-clients";
        _accessMinutes = s.GetValue<int?>("ExpiryMinutes") ?? 60;
        _refreshDays = s.GetValue<int?>("RefreshExpiryDays") ?? 30;
    }

    /// <summary>The claim names the JwtBearer validation should treat as the principal's name and role.</summary>
    public const string NameClaim = "uname";
    public const string RoleClaim = "role";

    /// <summary>Build a signed access token for the resolved user; returns the token and its absolute expiry.</summary>
    public (string Token, DateTime ExpiresAt) CreateAccessToken(AuthUserInfo user)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_accessMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(NameClaim, user.Username),
            new(DisplayNameClaim, user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (!string.IsNullOrEmpty(user.Email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        foreach (var roleId in user.RoleIds)
            claims.Add(new Claim(RoleClaim, roleId));
        foreach (var permission in user.Permissions)
            claims.Add(new Claim(AuthConstants.PermissionClaim, permission));

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return (_handler.WriteToken(token), expires);
    }

    /// <summary>Build a signed refresh token bound to the refresh audience, carrying only the subject id.</summary>
    public string CreateRefreshToken(string userId)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: AuthConstants.RefreshAudience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: now,
            expires: now.AddDays(_refreshDays),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return _handler.WriteToken(token);
    }

    /// <summary>Validate a refresh token (signature, issuer, refresh-audience, lifetime); returns the subject id or null.</summary>
    public string? ValidateRefreshToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var principal = _handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = AuthConstants.RefreshAudience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _key,
                ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);

            return principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        }
        catch
        {
            return null;
        }
    }
}
