// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Narrative;
using Domovoy.Narrative;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline tests for the diary's action/observation split (roadmap Epic 2N): telemetry readings must never
/// be narrated as family deeds («семья убавила датчик»), whatever trigger source they carry. Pure.
/// </summary>
public class ResolversTests
{
    [Fact]
    public void Hardware_sensor_reading_is_an_observation_not_a_family_deed()
    {
        var role = PersonaResolver.Resolve("device", "Zigbee2Mqtt", "climate_sensor", "temperature");
        Assert.Equal(PersonaRole.Impersonal, role);
    }

    [Fact]
    public void Telemetry_capability_on_an_actuator_is_still_an_observation()
    {
        // A smart plug reporting power draw is a measurement, not someone flipping it.
        var role = PersonaResolver.Resolve("device", "Zigbee2Mqtt", "switch", "power");
        Assert.Equal(PersonaRole.Impersonal, role);
    }

    [Fact]
    public void Motion_sensor_no_longer_narrates_as_residents_entering()
    {
        var role = PersonaResolver.Resolve("device", "Zigbee2Mqtt", "motion", "occupancy");
        Assert.Equal(PersonaRole.Impersonal, role);
    }

    [Fact]
    public void Actuator_actions_keep_their_personas()
    {
        Assert.Equal(PersonaRole.Residents, PersonaResolver.Resolve("user", "Zigbee2Mqtt", "light", "on_off"));
        Assert.Equal(PersonaRole.Residents, PersonaResolver.Resolve("device", "Zigbee2Mqtt", "contact", "contact"));
        Assert.Equal(PersonaRole.Spirit, PersonaResolver.Resolve("rule", "Zigbee2Mqtt", "light", "on_off"));
        Assert.Equal(PersonaRole.SpiritJudging, PersonaResolver.Resolve("ml", "Zigbee2Mqtt", "thermostat", "temperature_setpoint"));
    }

    [Fact]
    public void System_adapter_stays_impersonal_regardless_of_trigger()
    {
        Assert.Equal(PersonaRole.Impersonal, PersonaResolver.Resolve("user", "System", "sun", "is_dark"));
    }
}
