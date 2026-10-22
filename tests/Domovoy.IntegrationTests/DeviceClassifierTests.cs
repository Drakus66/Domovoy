// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;
using Domovoy.DbGateway.Services;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Pure unit tests for the device-archetype classifier (roadmap Epic 2D). No infrastructure — fast,
/// run without Docker.
/// </summary>
public sealed class DeviceClassifierTests
{
    private static string Classify(params Capability[] caps) => DeviceClassifier.Classify(caps);

    /// <summary>
    /// Архетипы 3D назначаются, а не оседают в «motion». Аудит 2026-08-07 предположил обратное — что
    /// объявленные `person`/`presence` никогда не присваиваются, — но виртуальные устройства присутствия
    /// приходят с `AdapterSource="System"` и моделью `system/person` / `system/presence`, а эту ветку
    /// классификатор разбирает раньше эвристик по capability. Тест закрепляет это: не догадка в отчёте,
    /// а проверяемый факт.
    /// </summary>
    [Fact]
    public void PresenceDevices_KeepTheirOwnArchetypes()
    {
        Assert.Equal(
            DeviceArchetypes.Person,
            DeviceClassifier.Classify(new[] { WellKnownCapabilities.Presence() }, "System", "system/person"));

        Assert.Equal(
            DeviceArchetypes.Presence,
            DeviceClassifier.Classify(
                new[] { WellKnownCapabilities.AnyoneHome(), WellKnownCapabilities.HomeCount() },
                "System", "system/presence"));
    }

    [Fact]
    public void Light_From_Brightness() =>
        Assert.Equal(DeviceArchetypes.Light, Classify(WellKnownCapabilities.OnOff(), WellKnownCapabilities.Brightness()));

    [Fact]
    public void Thermostat_From_Setpoint() =>
        Assert.Equal(DeviceArchetypes.Thermostat, Classify(WellKnownCapabilities.Temperature(), WellKnownCapabilities.TemperatureSetpoint()));

    [Fact]
    public void Motion_From_Occupancy() =>
        Assert.Equal(DeviceArchetypes.Motion, Classify(WellKnownCapabilities.Occupancy()));

    [Fact]
    public void Contact_From_Contact() =>
        Assert.Equal(DeviceArchetypes.Contact, Classify(WellKnownCapabilities.Contact()));

    [Fact]
    public void Lock_From_Lock() =>
        Assert.Equal(DeviceArchetypes.Lock, Classify(WellKnownCapabilities.Lock()));

    [Fact]
    public void Switch_From_Writable_OnOff_Only() =>
        Assert.Equal(DeviceArchetypes.Switch, Classify(WellKnownCapabilities.OnOff()));

    [Fact]
    public void ClimateSensor_From_ReadOnly_Environment() =>
        Assert.Equal(DeviceArchetypes.ClimateSensor, Classify(WellKnownCapabilities.Temperature(), WellKnownCapabilities.Humidity()));

    [Fact]
    public void EnergyMeter_From_Power() =>
        Assert.Equal(DeviceArchetypes.EnergyMeter, Classify(WellKnownCapabilities.Power()));

    [Fact]
    public void Sensor_From_ReadOnly_Battery() =>
        Assert.Equal(DeviceArchetypes.Sensor, Classify(WellKnownCapabilities.Battery()));

    [Fact]
    public void Unknown_From_NoCapabilities() =>
        Assert.Equal(DeviceArchetypes.Unknown, DeviceClassifier.Classify(Array.Empty<Capability>()));

    [Fact]
    public void ControlBlock_From_AdapterSource() =>
        Assert.Equal(DeviceArchetypes.ControlBlock,
            DeviceClassifier.Classify(new[] { WellKnownCapabilities.OnOff() }, adapterSource: "ControlBlock"));

    [Fact]
    public void ExplicitModel_Wins_Over_Capabilities() =>
        Assert.Equal(DeviceArchetypes.Lock,
            DeviceClassifier.Classify(new[] { WellKnownCapabilities.OnOff() }, adapterSource: "Zigbee2Mqtt", model: "lock"));
}
