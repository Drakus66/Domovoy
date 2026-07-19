// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// Åström–Hägglund <b>relay-feedback autotuner</b> for the <see cref="PidBlock"/> (roadmap Epic 2Q). Instead of
/// the PID law it drives a relay (the output flips between two levels around the setpoint, with a noise band for
/// hysteresis), which forces the closed loop into a steady oscillation — a limit cycle. From that oscillation's
/// period (T<sub>u</sub>) and amplitude (a) it recovers the ultimate gain K<sub>u</sub> = 4·d / (π·√(a²−ε²))
/// (d = relay half-amplitude, ε = noise band) and applies the classic Ziegler–Nichols PID rule.
/// <para>
/// Pure and self-contained: fed one <see cref="Step"/> per tick with the current measurement, it returns the
/// relay output to command and, once enough consistent cycles are seen, exposes <see cref="Result"/>. The first
/// recorded cycle is discarded as the start-up transient. State lives in fields (the block instance is reused
/// across ticks) — a mid-experiment restart simply starts the experiment over, which is fine for a one-off tune.
/// </para>
/// </summary>
public sealed class PidAutotune
{
    private readonly double _low;
    private readonly double _high;
    private readonly double _band;
    private readonly int _cycles;

    private bool _started;
    private bool _relayHigh;              // current relay direction (true → high output, pushing the pv up)
    private double _cycleMax;
    private double _cycleMin;
    private DateTimeOffset _cycleStart;   // time of the last trough (a down→up switch bounds a full cycle)
    private bool _haveCycleStart;
    private readonly List<double> _periods = new();
    private readonly List<double> _amplitudes = new();

    /// <summary>True once enough cycles have been observed and <see cref="Result"/> is computed.</summary>
    public bool Done { get; private set; }

    /// <summary>The recovered gains — valid only once <see cref="Done"/> is true.</summary>
    public PidGains Result { get; private set; }

    /// <param name="low">Relay low output level (e.g. the PID's outMin).</param>
    /// <param name="high">Relay high output level (e.g. the PID's outMax).</param>
    /// <param name="band">Noise band ε around the setpoint (pv units) — hysteresis that rejects measurement noise.</param>
    /// <param name="cycles">Full oscillation cycles to average after discarding the first (transient).</param>
    public PidAutotune(double low, double high, double band, int cycles = 4)
    {
        if (high <= low) throw new ArgumentException("relay high must exceed low");
        _low = low;
        _high = high;
        _band = Math.Max(band, 1e-6);
        _cycles = Math.Max(cycles, 2);
    }

    /// <summary>Advance the relay experiment one tick; returns the relay output to command this tick.</summary>
    public double Step(DateTimeOffset now, double pv, double sp)
    {
        if (Done) return _relayHigh ? _high : _low;

        if (!_started)
        {
            _started = true;
            _relayHigh = pv <= sp;         // start by driving pv toward the setpoint
            _cycleMax = _cycleMin = pv;
        }

        _cycleMax = Math.Max(_cycleMax, pv);
        _cycleMin = Math.Min(_cycleMin, pv);

        // Relay with a hysteresis band around the setpoint: full output when well below, off when well above.
        var next = _relayHigh;
        if (pv > sp + _band) next = false;
        else if (pv < sp - _band) next = true;

        if (next != _relayHigh)
        {
            // A down→up switch happens at a trough and bounds one full oscillation cycle.
            if (next)
            {
                if (_haveCycleStart)
                {
                    _periods.Add((now - _cycleStart).TotalSeconds);
                    _amplitudes.Add((_cycleMax - _cycleMin) / 2.0);
                }
                _cycleStart = now;
                _haveCycleStart = true;
                _cycleMax = _cycleMin = pv;
            }
            _relayHigh = next;
        }

        if (_periods.Count >= _cycles + 1)
            Finish();

        return _relayHigh ? _high : _low;
    }

    private void Finish()
    {
        // Discard the first (start-up) cycle, average the rest for a stable period + amplitude.
        var tu = _periods.Skip(1).Average();
        var a = _amplitudes.Skip(1).Average();
        var d = (_high - _low) / 2.0;
        var aEff = Math.Sqrt(Math.Max(a * a - _band * _band, 1e-9));
        var ku = 4.0 * d / (Math.PI * aEff);

        // Ziegler–Nichols PID: Kp=0.6·Ku, Ti=0.5·Tu, Td=0.125·Tu. This PidBlock's ki/kd are direct gains on
        // ∫e·dt and de, so ki = Kp/Ti = 1.2·Ku/Tu and kd = Kp·Td = 0.075·Ku·Tu.
        var kp = 0.6 * ku;
        var ki = tu > 0 ? 1.2 * ku / tu : 0;
        var kd = 0.075 * ku * tu;

        Result = new PidGains(kp, ki, kd, ku, tu);
        Done = true;
    }
}

/// <summary>Gains recovered by <see cref="PidAutotune"/>, plus the ultimate gain/period they came from.</summary>
public readonly record struct PidGains(double Kp, double Ki, double Kd, double Ku, double Tu);
