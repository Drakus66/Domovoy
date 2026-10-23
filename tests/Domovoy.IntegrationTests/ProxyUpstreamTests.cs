// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Reflection;
using System.Runtime.CompilerServices;

using Domovoy.ApiGateway.Controllers;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Pins the default upstream of every <see cref="ProxyController"/>. The base falls back to <c>db-gateway</c>, so a
/// controller that forgets its override compiles, starts and answers 404 from the wrong service — exactly how the
/// plugin registry and the assistant went dark after the proxy consolidation. A new proxy controller has to be
/// added here, which forces its upstream to be a deliberate choice.
/// </summary>
public sealed class ProxyUpstreamTests
{
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["AssistantController"] = "automation-service",
        ["AutomationsController"] = "db-gateway",
        ["BackupController"] = "db-gateway",
        ["BlocksController"] = "db-gateway",
        ["CapabilityDevicesController"] = "db-gateway",
        ["DashboardsController"] = "db-gateway",
        ["EnergyController"] = "db-gateway",
        ["HistoryController"] = "db-gateway",
        ["HomeStoryController"] = "db-gateway",
        ["MlController"] = "db-gateway",
        ["ModeController"] = "db-gateway",
        ["NotificationsController"] = "db-gateway",
        ["PluginsController"] = "plugin-supervisor",
        ["PowerTopologyController"] = "db-gateway",
        ["ProposalsController"] = "db-gateway",
        ["ResidentsController"] = "db-gateway",
        ["RolesController"] = "db-gateway",
        ["ScenesController"] = "db-gateway",
        ["SettingsController"] = "db-gateway",
        ["UpdatesController"] = "db-gateway",
        ["UsersController"] = "db-gateway",
        ["VariablesController"] = "db-gateway",
        ["ZonesController"] = "db-gateway",
    };

    private static IEnumerable<Type> ProxyControllers() =>
        typeof(ProxyController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ProxyController).IsAssignableFrom(t));

    [Fact]
    public void EveryProxyController_HasPinnedUpstream()
    {
        var actual = ProxyControllers().ToDictionary(t => t.Name, UpstreamOf);
        Assert.Equal(Expected.OrderBy(p => p.Key), actual.OrderBy(p => p.Key));
    }

    private static string UpstreamOf(Type controller)
    {
        // The getter is a constant per type, so an uninitialized instance is enough — no DI, no HttpClientFactory.
        var instance = RuntimeHelpers.GetUninitializedObject(controller);
        var property = typeof(ProxyController).GetProperty("UpstreamClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (string)property.GetValue(instance)!;
    }
}
