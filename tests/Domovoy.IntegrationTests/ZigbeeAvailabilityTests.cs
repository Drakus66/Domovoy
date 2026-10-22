// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Connectivity.Adapters;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Pure unit tests for the Zigbee2MQTT per-device availability parsing. This is the canonical per-device
/// liveness signal (z2m 'availability:' feature): a device that stops responding while the bridge stays up
/// (coordinator lost / device unplugged) publishes <c>zigbee2mqtt/&lt;friendly&gt;/availability=offline</c>,
/// which the adapter turns into a <c>DeviceOnlineChangedV1</c> so it is no longer stuck online in the UI.
/// </summary>
public sealed class ZigbeeAvailabilityTests
{
    [Theory]
    [InlineData("zigbee2mqtt/living_lamp/availability", "living_lamp")]
    [InlineData("zigbee2mqtt/kitchen_sensor/availability", "kitchen_sensor")]
    public void FriendlyNameFromAvailabilityTopic_ExtractsFriendly(string topic, string expected)
    {
        Assert.Equal(expected, Zigbee2MqttAdapter.FriendlyNameFromAvailabilityTopic(topic));
    }

    [Theory]
    [InlineData("zigbee2mqtt/living_lamp")]            // plain state topic, not availability
    [InlineData("zigbee2mqtt/bridge/state")]            // bridge topic
    [InlineData("zigbee2mqtt/availability")]            // no friendly-name segment
    [InlineData("homeassistant/x/availability")]        // wrong prefix
    public void FriendlyNameFromAvailabilityTopic_RejectsNonAvailability(string topic)
    {
        Assert.Null(Zigbee2MqttAdapter.FriendlyNameFromAvailabilityTopic(topic));
    }

    [Theory]
    [InlineData("online", true)]
    [InlineData("offline", false)]
    [InlineData("\"online\"", true)]                     // quoted string payload
    [InlineData("{\"state\":\"online\"}", true)]          // modern z2m JSON form
    [InlineData("{\"state\":\"offline\"}", false)]
    [InlineData(" {\"state\":\"online\"} ", true)]        // whitespace-padded
    public void ParseAvailability_Recognized(string payload, bool expectedOnline)
    {
        Assert.Equal(expectedOnline, Zigbee2MqttAdapter.ParseAvailability(payload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("maybe")]
    [InlineData("{\"foo\":\"bar\"}")]                     // JSON without a state field
    [InlineData("{ not json")]                            // malformed JSON
    public void ParseAvailability_Unrecognized_ReturnsNull(string payload)
    {
        Assert.Null(Zigbee2MqttAdapter.ParseAvailability(payload));
    }
}
