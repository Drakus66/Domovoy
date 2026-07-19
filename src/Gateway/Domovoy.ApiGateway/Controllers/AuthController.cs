// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;
using System.Net.Http.Json;

using Domovoy.ApiGateway.Services;
using Domovoy.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Local authentication edge (mobile-app / remote-access track). Verifies credentials against the DbGateway
/// credential store, mints the JWT access/refresh pair, and exposes the current identity. This is the ONLY
/// public surface that touches passwords; the plaintext is forwarded once to the in-cluster DbGateway and never
/// stored here.
///
/// <para><b>Enforcement flag.</b> When <c>JwtSettings:Enabled</c> is off (dev / integration tests) every endpoint
/// returns a synthetic admin session so the UI's login flow is a no-op and the API stays open. When on, login is
/// real and the rest of the gateway is gated by the fallback + permission policies.</para>
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JwtTokenService _jwt;
    private readonly bool _enabled;
    private readonly ILogger<AuthController> _logger;

    // Lightweight in-memory login throttle. Single-process gateway, single household — a static map keyed by
    // username+client-ip is enough to blunt online guessing; a lost restart just clears the counters.
    private static readonly ConcurrentDictionary<string, (int Failures, DateTime LockedUntil)> Attempts = new();
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(5);

    public AuthController(
        IHttpClientFactory httpClientFactory,
        JwtTokenService jwt,
        IConfiguration config,
        ILogger<AuthController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _jwt = jwt;
        _enabled = config.GetSection("JwtSettings").GetValue<bool>("Enabled");
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (!_enabled)
            return Ok(SyntheticSession());

        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return Unauthorized(new { error = "invalid credentials" });

        var throttleKey = $"{req.Username.Trim().ToLowerInvariant()}|{ClientIp()}";
        if (IsLockedOut(throttleKey, out var retryAfter))
        {
            Response.Headers["Retry-After"] = ((int)retryAfter.TotalSeconds).ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "too many attempts, try again later" });
        }

        var client = _httpClientFactory.CreateClient("db-gateway");
        VerifyCredentialsResponse? verify;
        try
        {
            var upstream = await client.PostAsJsonAsync(
                "api/auth/verify-credentials",
                new VerifyCredentialsRequest(req.Username.Trim(), req.Password), ct);
            verify = await upstream.Content.ReadFromJsonAsync<VerifyCredentialsResponse>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Credential verification failed to reach the DbGateway");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "auth backend unavailable" });
        }

        if (verify is null || !verify.Success || verify.User is null)
        {
            RegisterFailure(throttleKey);
            return Unauthorized(new { error = "invalid credentials" });
        }

        RegisterSuccess(throttleKey);
        return Ok(BuildSession(verify.User));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        if (!_enabled)
            return Ok(SyntheticSession());

        var userId = _jwt.ValidateRefreshToken(req.RefreshToken);
        if (userId is null)
            return Unauthorized(new { error = "invalid refresh token" });

        var client = _httpClientFactory.CreateClient("db-gateway");
        AuthUserInfo? user;
        try
        {
            var upstream = await client.GetAsync($"api/auth/user-info/{Uri.EscapeDataString(userId)}", ct);
            if (!upstream.IsSuccessStatusCode)
                return Unauthorized(new { error = "user no longer valid" });
            user = await upstream.Content.ReadFromJsonAsync<AuthUserInfo>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed to reach the DbGateway");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "auth backend unavailable" });
        }

        return user is null
            ? Unauthorized(new { error = "user no longer valid" })
            : Ok(BuildSession(user));
    }

    // Protected by the fallback policy when enforcement is on; open (and synthetic) when it's off.
    [HttpGet("me")]
    public IActionResult Me()
    {
        if (!_enabled)
            return Ok(SyntheticSession().User);

        var id = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(id))
            return Unauthorized();

        var info = new AuthUserInfo(
            id,
            User.FindFirst(JwtTokenService.NameClaim)?.Value ?? string.Empty,
            User.FindFirst(JwtTokenService.DisplayNameClaim)?.Value ?? string.Empty,
            User.FindFirst("email")?.Value,
            User.FindAll(JwtTokenService.RoleClaim).Select(c => c.Value).ToList(),
            User.FindAll(AuthConstants.PermissionClaim).Select(c => c.Value).ToList());
        return Ok(info);
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // v1 is stateless: the client drops its tokens. Server-side revocation lives with disabling the user
        // (refresh re-checks Enabled, so a disabled account dies within one access-token lifetime).
        return NoContent();
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] SetPasswordRequest req, CancellationToken ct)
    {
        if (!_enabled)
            return NoContent();

        var id = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(id))
            return Unauthorized();

        // Self-service change: the current password is mandatory here (an admin reset of someone else's password
        // goes through UsersController → users.manage, not this self endpoint).
        if (string.IsNullOrWhiteSpace(req.CurrentPassword))
            return BadRequest(new { error = "current password is required" });

        var client = _httpClientFactory.CreateClient("db-gateway");
        var upstream = await client.PutAsJsonAsync(
            $"api/users/{Uri.EscapeDataString(id)}/password",
            new SetPasswordRequest(req.NewPassword, req.CurrentPassword), ct);

        return StatusCode((int)upstream.StatusCode);
    }

    private LoginResponse BuildSession(AuthUserInfo user)
    {
        var (access, expiresAt) = _jwt.CreateAccessToken(user);
        var refresh = _jwt.CreateRefreshToken(user.Id);
        return new LoginResponse(access, refresh, expiresAt, user);
    }

    // Dev/no-enforcement session: an admin with the full permission set and empty tokens (the WebUI treats an
    // empty access token as "auth disabled, send no Authorization header").
    private static LoginResponse SyntheticSession()
    {
        var user = new AuthUserInfo(
            "dev-admin",
            "admin",
            "Administrator (dev)",
            null,
            new List<string> { "admin" },
            WellKnownPermissions.All.ToList());
        return new LoginResponse(string.Empty, string.Empty, DateTime.UtcNow.AddYears(1), user);
    }

    private string ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static bool IsLockedOut(string key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (Attempts.TryGetValue(key, out var state) && state.Failures >= MaxFailures)
        {
            var remaining = state.LockedUntil - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                retryAfter = remaining;
                return true;
            }
            Attempts.TryRemove(key, out _);
        }
        return false;
    }

    private static void RegisterFailure(string key)
    {
        Attempts.AddOrUpdate(
            key,
            _ => (1, DateTime.UtcNow.Add(LockoutWindow)),
            (_, prev) => (prev.Failures + 1, DateTime.UtcNow.Add(LockoutWindow)));
    }

    private static void RegisterSuccess(string key) => Attempts.TryRemove(key, out _);
}
