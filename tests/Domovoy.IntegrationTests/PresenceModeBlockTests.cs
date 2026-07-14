// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Capabilities;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the <c>presence_mode</c> block — the user-owned replacement for the hardcoded 1G
/// PresenceMonitor: any presence → home, silence past the delay → away, manual modes never overridden,
/// restart never flips the home by itself.
/// </summary>
public class PresenceModeBlockTests
{
    private static FakeBlockCtx Ctx() => new();

    [Fact]
    public void Presence_Emits_HomeMode()
    {
        var block = new PresenceModeBlock();
        var ctx = Ctx();
        ctx.Inputs["presence_a"] = true;

        block.Tick(ctx);

        Assert.Equal("Home", ctx.Get(CapabilityIds.HomeMode));
        Assert.True(ctx.GetBool("presence_any"));
    }

    [Fact]
    public void Silence_Before_Delay_Emits_NoMode()
    {
        var block = new PresenceModeBlock();
        var ctx = Ctx();
        ctx.Inputs["presence_a"] = false;

        block.Tick(ctx); // seeds lastPresenceAt = now
        ctx.Now = ctx.Now.AddMinutes(5);
        block.Tick(ctx);

        Assert.Null(ctx.Get(CapabilityIds.HomeMode));
        Assert.False(ctx.GetBool("presence_any"));
    }

    [Fact]
    public void Silence_Past_Delay_Emits_AwayMode()
    {
        var block = new PresenceModeBlock();
        var ctx = Ctx();
        ctx.Params["awayDelayMin"] = 10;
        ctx.Inputs["presence_a"] = false;

        block.Tick(ctx); // seed
        ctx.Now = ctx.Now.AddMinutes(11);
        block.Tick(ctx);

        Assert.Equal("Away", ctx.Get(CapabilityIds.HomeMode));
    }

    [Fact]
    public void Restart_Seeds_Timer_And_Does_Not_Flip_Away()
    {
        // First tick after a (re)start with silent sensors must seed the timer, not switch to Away.
        var block = new PresenceModeBlock();
        var ctx = Ctx();
        ctx.Inputs["presence_a"] = false;

        block.Tick(ctx);

        Assert.Null(ctx.Get(CapabilityIds.HomeMode));
    }

    [Fact]
    public void Manual_Mode_Guard_Blocks_Emission()
    {
        var block = new PresenceModeBlock();
        var ctx = Ctx();
        ctx.Inputs["presence_a"] = true;
        ctx.Inputs["current_mode"] = "Night"; // occupant's explicit choice — presence must not override

        block.Tick(ctx);

        Assert.Null(ctx.Get(CapabilityIds.HomeMode));
        Assert.True(ctx.GetBool("presence_any")); // observational output still emitted
    }

    [Fact]
    public void Managed_Mode_Allows_Emission()
    {
        var block = new PresenceModeBlock();
        var ctx = Ctx();
        ctx.Inputs["presence_a"] = true;
        ctx.Inputs["current_mode"] = "Away";

        block.Tick(ctx);

        Assert.Equal("Home", ctx.Get(CapabilityIds.HomeMode));
    }

    [Fact]
    public void Any_Of_Multiple_Sensors_Counts_As_Present()
    {
        var block = new PresenceModeBlock();
        var ctx = Ctx();
        ctx.Inputs["presence_a"] = false;
        ctx.Inputs["presence_b"] = false;
        ctx.Inputs["presence_c"] = true;

        block.Tick(ctx);

        Assert.Equal("Home", ctx.Get(CapabilityIds.HomeMode));
    }

    [Fact]
    public void No_Bound_Sensors_Does_Nothing()
    {
        var block = new PresenceModeBlock();
        var ctx = Ctx();

        block.Tick(ctx);

        Assert.Empty(ctx.Emitted);
    }

    [Fact]
    public void Custom_Mode_Options_Are_Used()
    {
        var block = new PresenceModeBlock();
        var ctx = Ctx();
        ctx.Options["homeMode"] = "Home";
        ctx.Options["awayMode"] = "Vacation";
        ctx.Params["awayDelayMin"] = 1;
        ctx.Inputs["presence_a"] = false;

        block.Tick(ctx); // seed
        ctx.Now = ctx.Now.AddMinutes(2);
        block.Tick(ctx);

        Assert.Equal("Vacation", ctx.Get(CapabilityIds.HomeMode));
    }

    [Fact]
    public void Presence_Return_After_Silence_Switches_Back_Home()
    {
        var block = new PresenceModeBlock();
        var ctx = Ctx();
        ctx.Params["awayDelayMin"] = 10;
        ctx.Inputs["presence_a"] = false;

        block.Tick(ctx); // seed
        ctx.Now = ctx.Now.AddMinutes(11);
        block.Tick(ctx);
        Assert.Equal("Away", ctx.Get(CapabilityIds.HomeMode));

        ctx.Inputs["presence_a"] = true;
        ctx.Inputs["current_mode"] = "Away";
        ctx.Now = ctx.Now.AddMinutes(1);
        block.Tick(ctx);

        Assert.Equal("Home", ctx.Get(CapabilityIds.HomeMode));
    }
}
