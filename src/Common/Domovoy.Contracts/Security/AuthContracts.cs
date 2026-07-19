// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Security;

/// <summary>
/// Authentication DTOs shared by the two gateways (mobile-app / remote-access track). The ApiGateway is the
/// public edge that mints/validates JWTs; the DbGateway owns the credential store and verifies passwords. These
/// cross the gateway boundary, so they live in Contracts alongside <see cref="User"/> / <see cref="Role"/>.
///
/// <para>Design: the ApiGateway never sees a password hash. It POSTs the plaintext credential to the DbGateway's
/// internal <c>/api/auth/verify-credentials</c> (in-cluster only, not exposed by nginx), gets back a resolved
/// <see cref="AuthUserInfo"/> on success, and turns that into a signed access token. Refresh re-resolves the
/// user by id so permission changes and disabling take effect within one access-token lifetime.</para>
/// </summary>
public static class AuthConstants
{
    /// <summary>Audience stamped on refresh tokens so an access token can't be replayed as a refresh token.</summary>
    public const string RefreshAudience = "domovoy-refresh";

    /// <summary>Claim type carrying one granted permission (one claim per permission).</summary>
    public const string PermissionClaim = "perm";
}

/// <summary>Login request: a username + password from the client.</summary>
public record LoginRequest(string Username, string Password);

/// <summary>The resolved identity handed to the client and embedded (as claims) in the access token.</summary>
public record AuthUserInfo(
    string Id,
    string Username,
    string DisplayName,
    string? Email,
    List<string> RoleIds,
    List<string> Permissions);

/// <summary>Successful login/refresh result: the token pair + the resolved user.</summary>
public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    AuthUserInfo User);

/// <summary>Exchange a still-valid refresh token for a fresh access token (and a rotated refresh token).</summary>
public record RefreshRequest(string RefreshToken);

/// <summary>Internal DbGateway request to verify a plaintext credential against the stored hash.</summary>
public record VerifyCredentialsRequest(string Username, string Password);

/// <summary>Internal DbGateway result: on success carries the resolved user (roles → union of permissions).</summary>
public record VerifyCredentialsResponse(bool Success, AuthUserInfo? User);

/// <summary>
/// Set or change a user's password. When <see cref="CurrentPassword"/> is provided it is verified first
/// (self-service change); when omitted this is an admin reset (the ApiGateway gates that on <c>users.manage</c>).
/// </summary>
public record SetPasswordRequest(string NewPassword, string? CurrentPassword);
