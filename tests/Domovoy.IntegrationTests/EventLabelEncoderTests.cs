// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.AutomationService.Ml.Templates;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the event → ML label encoder (roadmap Epic 2I, Phase 1): boolean/enum targets are labeled
/// from state-change events the way numeric targets are from telemetry.
/// </summary>
public sealed class EventLabelEncoderTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Theory]
    [InlineData("true", 1)]
    [InlineData("false", 0)]
    [InlineData("\"on\"", 1)]
    [InlineData("\"OFF\"", 0)]
    [InlineData("\"open\"", 1)]
    [InlineData("\"closed\"", 0)]
    [InlineData("1", 1)]
    [InlineData("0", 0)]
    [InlineData("21.5", 21.5)]
    public void Encodes_BooleanAndNumericValues(string raw, double expected) =>
        Assert.Equal(expected, EventLabelEncoder.ToLabel(Json(raw)));

    [Fact]
    public void ReturnsNull_ForUnusableValues()
    {
        Assert.Null(EventLabelEncoder.ToLabel(null));
        Assert.Null(EventLabelEncoder.ToLabel(Json("\"banana\"")));
        Assert.Null(EventLabelEncoder.ToLabel(Json("{}")));
    }
}
