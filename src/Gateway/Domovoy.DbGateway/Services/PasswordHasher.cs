// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Security.Cryptography;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Password hashing for local accounts (mobile-app / remote-access track). Uses <b>PBKDF2-HMAC-SHA256</b> via the
/// BCL's <see cref="Rfc2898DeriveBytes"/> — deliberately no third-party crypto dependency (keeps the AGPL +
/// commercial dual-license clean and avoids a supply-chain surface for the credential path). Iteration count
/// follows the OWASP 2023 PBKDF2-SHA256 recommendation.
///
/// <para>The stored string is self-describing: <c>v1.{iterations}.{saltBase64}.{keyBase64}</c>. Base64 never
/// contains a '.', so splitting on '.' is unambiguous, and the embedded iteration count lets us raise the work
/// factor later while still verifying older hashes.</para>
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;          // 128-bit salt
    private const int KeySize = 32;           // 256-bit derived key
    private const int Iterations = 210_000;   // OWASP 2023 PBKDF2-HMAC-SHA256
    private const string Prefix = "v1";
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>Hash a plaintext password into the self-describing storage format.</summary>
    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return string.Join('.', Prefix, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    /// <summary>
    /// Constant-time verify of <paramref name="password"/> against a stored hash. Returns false for any
    /// null/blank/malformed stored value (a user with no password can never authenticate).
    /// </summary>
    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != Prefix) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
