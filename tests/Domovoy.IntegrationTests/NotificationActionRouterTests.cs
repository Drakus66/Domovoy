// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.ApiGateway.Services;
using Domovoy.Contracts.Notifications;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline unit tests for the Epic 3F actionable-notification router (<see cref="NotificationActionRouter"/>):
/// maps a button to an execution plan (forward to a DbGateway endpoint / a bus device command) and rejects
/// missing params or client-only kinds. Pure — the controller just carries out the plan.
/// </summary>
public sealed class NotificationActionRouterTests
{
    private const string Actor = "user:anya";

    private static NotificationAction Action(string kind, params (string, string)[] p) =>
        new("id", "Label", kind, p.ToDictionary(x => x.Item1, x => x.Item2));

    [Fact]
    public void ApproveProposal_ForwardsToApprovePath()
    {
        var plan = NotificationActionRouter.Resolve(
            Action(NotificationActionKinds.ApproveProposal, ("proposalId", "p-42")), Actor);

        Assert.Null(plan.Error);
        Assert.False(plan.IsDeviceCommand);
        Assert.Equal("db-gateway", plan.HttpClient);
        Assert.Equal("POST", plan.HttpMethod);
        Assert.Equal("api/proposals/p-42/approve", plan.HttpPath);
    }

    [Fact]
    public void RejectProposal_ForwardsToRejectPath()
    {
        var plan = NotificationActionRouter.Resolve(
            Action(NotificationActionKinds.RejectProposal, ("proposalId", "p-9")), Actor);

        Assert.Equal("api/proposals/p-9/reject", plan.HttpPath);
    }

    [Fact]
    public void ApproveProposal_MissingId_IsRejected()
    {
        var plan = NotificationActionRouter.Resolve(Action(NotificationActionKinds.ApproveProposal), Actor);
        Assert.NotNull(plan.Error);
    }

    [Fact]
    public void SetMode_ForwardsToModeWithActorSource()
    {
        var plan = NotificationActionRouter.Resolve(
            Action(NotificationActionKinds.SetMode, ("mode", "away")), Actor);

        Assert.Equal("PUT", plan.HttpMethod);
        Assert.Equal("api/mode", plan.HttpPath);
        Assert.Contains("away", plan.HttpJsonBody);
        Assert.Contains(Actor, plan.HttpJsonBody); // the command is attributed to the tapping user
    }

    [Fact]
    public void DeviceCommand_BecomesABusCommand_WithParsedValue()
    {
        var id = Guid.NewGuid();
        var plan = NotificationActionRouter.Resolve(
            Action(NotificationActionKinds.DeviceCommand, ("deviceId", id.ToString()), ("capabilityId", "on_off"), ("value", "true")),
            Actor);

        Assert.Null(plan.Error);
        Assert.True(plan.IsDeviceCommand);
        Assert.Equal(id, plan.DeviceId);
        Assert.Equal("on_off", plan.CapabilityId);
        Assert.Equal(true, plan.Value);
    }

    [Fact]
    public void DeviceCommand_BadGuid_IsRejected()
    {
        var plan = NotificationActionRouter.Resolve(
            Action(NotificationActionKinds.DeviceCommand, ("deviceId", "nope"), ("capabilityId", "on_off")), Actor);
        Assert.NotNull(plan.Error);
    }

    [Fact]
    public void ClientOnlyKind_IsRejectedServerSide()
    {
        var plan = NotificationActionRouter.Resolve(
            Action(NotificationActionKinds.Open, ("route", "/proposals")), Actor);
        Assert.NotNull(plan.Error);
    }

    [Theory]
    [InlineData("true", typeof(bool))]
    [InlineData("42", typeof(long))]
    [InlineData("3.5", typeof(double))]
    [InlineData("warm", typeof(string))]
    public void ParseValue_CoercesByShape(string raw, Type expected)
    {
        var value = NotificationActionRouter.ParseValue(raw);
        Assert.NotNull(value);
        Assert.IsType(expected, value);
    }
}
