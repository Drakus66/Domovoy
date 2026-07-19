// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the Åström–Hägglund relay-feedback PID autotuner (roadmap Epic 2Q). Each test closes the loop
/// with a simple first-order-lag process so the relay induces a real limit cycle, then checks the recovered
/// Ziegler–Nichols gains are sane. Pure — no infrastructure, deterministic (fixed dt, no wall clock).
/// </summary>
public sealed class PidAutotuneTests
{
    private const double Sp = 21, Tau = 4, Dt = 0.5;

    // Run the tuner in closed loop with a first-order lag pv' = (K·u − pv)/tau, returning the finished tuner.
    private static PidAutotune RunLoop(double processGain, double band = 0.5, int cycles = 4)
    {
        var tuner = new PidAutotune(low: 0, high: 100, band: band, cycles: cycles);
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var pv = Sp;
        for (var i = 0; i < 20000 && !tuner.Done; i++)
        {
            var u = tuner.Step(t, pv, Sp);
            pv += (Dt / Tau) * (processGain * u - pv);
            t = t.AddSeconds(Dt);
        }
        return tuner;
    }

    [Fact]
    public void Autotune_RecoversPositiveGains_FromRelayLimitCycle()
    {
        var tuner = RunLoop(processGain: 0.42);

        Assert.True(tuner.Done, "autotune did not converge within the tick budget");
        var g = tuner.Result;
        Assert.True(g.Ku > 0 && g.Tu > 0, $"expected Ku,Tu > 0 but got Ku={g.Ku}, Tu={g.Tu}");
        Assert.True(g.Kp > 0, $"kp={g.Kp}");
        Assert.True(g.Ki > 0, $"ki={g.Ki}");
        Assert.True(g.Kd > 0, $"kd={g.Kd}");

        // Ziegler–Nichols relationships must hold exactly between the recovered numbers.
        Assert.Equal(0.6 * g.Ku, g.Kp, 6);
        Assert.Equal(1.2 * g.Ku / g.Tu, g.Ki, 6);
        Assert.Equal(0.075 * g.Ku * g.Tu, g.Kd, 6);
    }

    [Fact]
    public void Autotune_ExercisesBothRelayExtremes()
    {
        var tuner = new PidAutotune(0, 100, 0.5, 4);
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var pv = Sp;
        bool sawLow = false, sawHigh = false;
        for (var i = 0; i < 20000 && !tuner.Done; i++)
        {
            var u = tuner.Step(t, pv, Sp);
            sawLow |= u == 0;
            sawHigh |= u == 100;
            pv += (Dt / Tau) * (0.42 * u - pv);
            t = t.AddSeconds(Dt);
        }
        Assert.True(sawLow && sawHigh, "relay should drive the output to both outMin and outMax");
    }

    [Fact]
    public void Autotune_HigherProcessGain_YieldsLowerUltimateGain()
    {
        // A more responsive process swings wider under the relay → larger amplitude → lower ultimate gain Ku.
        var slow = RunLoop(processGain: 0.42).Result.Ku;
        var fast = RunLoop(processGain: 0.84).Result.Ku;
        Assert.True(fast < slow, $"expected Ku to fall as process gain rises: fast={fast} !< slow={slow}");
    }

    [Fact]
    public void Autotune_RejectsInvalidRelayLevels()
    {
        Assert.Throws<ArgumentException>(() => new PidAutotune(low: 100, high: 0, band: 0.5));
    }
}
