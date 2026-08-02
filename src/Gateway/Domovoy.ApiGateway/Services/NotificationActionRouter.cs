// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.Contracts.Notifications;

namespace Domovoy.ApiGateway.Services;

/// <summary>
/// Turns an actionable-notification button (roadmap Epic 3F) into a concrete execution plan: either a forward to
/// a DbGateway endpoint (approve/reject a proposal, switch mode) or a device command published on the bus. Pure +
/// infrastructure-free so the validation (required params, GUID shape, server-side-only kinds) is unit-testable
/// without spinning up the gateway; <see cref="Controllers.NotificationsController"/> just executes the plan.
/// </summary>
public static class NotificationActionRouter
{
    /// <summary>An action resolved to what the controller should do. Exactly one of <see cref="Error"/>,
    /// <see cref="IsDeviceCommand"/> (bus) or an HTTP forward (<see cref="HttpPath"/>) is meaningful.</summary>
    public sealed record Plan(
        string Kind,
        bool IsDeviceCommand = false,
        string? HttpClient = null,
        string? HttpMethod = null,
        string? HttpPath = null,
        string? HttpJsonBody = null,
        Guid DeviceId = default,
        string? CapabilityId = null,
        object? Value = null,
        string? Error = null);

    /// <summary>Resolve an action for the given actor (used as the mode/command attribution source).</summary>
    public static Plan Resolve(NotificationAction action, string actor)
    {
        var p = action.Params ?? new Dictionary<string, string>();
        string? Get(string k) => p.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        switch (action.Kind)
        {
            case NotificationActionKinds.ApproveProposal:
            case NotificationActionKinds.RejectProposal:
            {
                var id = Get("proposalId");
                if (id is null) return Invalid(action.Kind, "proposalId is required");
                var verb = action.Kind == NotificationActionKinds.ApproveProposal ? "approve" : "reject";
                return new Plan(action.Kind, HttpClient: "db-gateway", HttpMethod: "POST",
                    HttpPath: $"api/proposals/{Uri.EscapeDataString(id)}/{verb}");
            }

            case NotificationActionKinds.SetMode:
            {
                var mode = Get("mode");
                if (mode is null) return Invalid(action.Kind, "mode is required");
                var body = JsonSerializer.Serialize(new { mode, source = actor });
                return new Plan(action.Kind, HttpClient: "db-gateway", HttpMethod: "PUT",
                    HttpPath: "api/mode", HttpJsonBody: body);
            }

            case NotificationActionKinds.DeviceCommand:
            {
                var deviceId = Get("deviceId");
                var cap = Get("capabilityId");
                if (deviceId is null || cap is null) return Invalid(action.Kind, "deviceId and capabilityId are required");
                if (!Guid.TryParse(deviceId, out var gid)) return Invalid(action.Kind, "deviceId is not a valid GUID");
                return new Plan(action.Kind, IsDeviceCommand: true, DeviceId: gid, CapabilityId: cap, Value: ParseValue(Get("value")));
            }

            default:
                return Invalid(action.Kind, "not a server-side action");
        }
    }

    /// <summary>Coerce a string action param to bool/number/string, mirroring device-control normalization.</summary>
    public static object? ParseValue(string? raw)
    {
        if (raw is null) return null;
        if (bool.TryParse(raw, out var b)) return b;
        if (long.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var l)) return l;
        if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        return raw;
    }

    private static Plan Invalid(string kind, string error) => new(kind, Error: error);
}
