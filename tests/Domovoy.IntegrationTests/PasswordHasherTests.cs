// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Services;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline unit tests for the local-account password hasher (mobile-app / remote-access track). Asserts the
/// PBKDF2 round-trip, that a wrong/blank/tampered credential fails closed, and that the self-describing storage
/// format is what we expect (so an iteration bump can still verify old hashes).
/// </summary>
public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_Succeeds()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");
        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        var hash = PasswordHasher.Hash("s3cret-password");
        Assert.False(PasswordHasher.Verify("S3cret-password", hash));   // case-sensitive
        Assert.False(PasswordHasher.Verify("s3cret-passwor", hash));    // truncated
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Verify_NullOrBlankStored_FailsClosed()
    {
        Assert.False(PasswordHasher.Verify("anything", null));
        Assert.False(PasswordHasher.Verify("anything", ""));
        Assert.False(PasswordHasher.Verify("anything", "not-a-valid-hash"));
        Assert.False(PasswordHasher.Verify("anything", "v1.badstuff"));
    }

    [Fact]
    public void Hash_UsesSelfDescribingFormat_WithRandomSalt()
    {
        var a = PasswordHasher.Hash("same-password");
        var b = PasswordHasher.Hash("same-password");

        Assert.StartsWith("v1.", a);
        Assert.Equal(4, a.Split('.').Length);
        Assert.NotEqual(a, b);                                  // random salt → different stored strings
        Assert.True(PasswordHasher.Verify("same-password", a)); // ...both still verify
        Assert.True(PasswordHasher.Verify("same-password", b));
    }

    [Fact]
    public void Verify_TamperedHash_Fails()
    {
        var hash = PasswordHasher.Hash("original");
        var parts = hash.Split('.');
        // Flip the last character of the derived-key segment.
        var key = parts[3];
        var flipped = key[..^1] + (key[^1] == 'A' ? 'B' : 'A');
        var tampered = string.Join('.', parts[0], parts[1], parts[2], flipped);

        Assert.False(PasswordHasher.Verify("original", tampered));
    }
}
