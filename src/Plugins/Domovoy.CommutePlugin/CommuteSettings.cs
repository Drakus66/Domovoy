// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Plugins;

namespace Domovoy.CommutePlugin;

/// <summary>
/// The settings the Commute plugin exposes to the UI (roadmap Epic 2M tail — plugin settings). These replace
/// the <c>COMMUTE__PROVIDER</c>/<c>COMMUTE__TOMTOMAPIKEY</c> docker-compose environment: the operator now picks
/// the traffic backend (offline simulator vs. live TomTom), supplies the key, and tunes the simulator from the
/// plugins panel. Changes are announced/applied over the bus and take effect live (no restart).
/// </summary>
public static class CommuteSettings
{
    /// <summary>Must match the plugin.json id so the supervisor keys the settings to this plugin's panel card.</summary>
    public const string PluginId = "commute-planner";

    public const string Provider = "provider";
    public const string TomTomApiKey = "tomTomApiKey";
    public const string AvgSpeedKmh = "avgSpeedKmh";
    public const string FixedOverheadMinutes = "fixedOverheadMinutes";

    public const string ProviderSimulated = "simulated";
    public const string ProviderTomTom = "tomtom";

    public static readonly IReadOnlyList<PluginSettingDescriptor> Descriptors = new List<PluginSettingDescriptor>
    {
        new()
        {
            Key = Provider,
            Kind = PluginSettingKinds.Enum,
            Label = "Источник трафика",
            Description = "Офлайн-симулятор работает без ключа и интернета; TomTom даёт живой трафик (нужен ключ).",
            Values = new List<string> { ProviderSimulated, ProviderTomTom },
            Default = ProviderSimulated,
        },
        new()
        {
            Key = TomTomApiKey,
            Kind = PluginSettingKinds.Secret,
            Label = "Ключ TomTom API",
            Description = "Используется, когда источник трафика — TomTom. Хранится на сервере, в UI не показывается.",
            Secret = true,
        },
        new()
        {
            Key = AvgSpeedKmh,
            Kind = PluginSettingKinds.Number,
            Label = "Средняя скорость (симулятор)",
            Unit = "км/ч",
            Min = 10,
            Max = 130,
            Step = 1,
            Default = 45.0,
        },
        new()
        {
            Key = FixedOverheadMinutes,
            Kind = PluginSettingKinds.Number,
            Label = "Накладные минуты (симулятор)",
            Description = "Парковка, пешая часть — добавляется к каждой поездке.",
            Unit = "мин",
            Min = 0,
            Max = 60,
            Step = 1,
            Default = 3.0,
        },
    };
}
