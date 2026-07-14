// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Native;
using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Backstop that expires stale device liveness. Native devices (and the emulator) re-assert their
/// availability on a periodic heartbeat; when a device stops being heard from — it dropped silently, its
/// offline signal was missed (retained Last-Will not redelivered, or Connectivity was down at the time),
/// or the row is a leftover left <c>IsOnline=true</c> from before a restart — nothing ever flips it
/// offline. This watchdog periodically marks such devices offline once their <c>LastUpdated</c> is older
/// than <see cref="LivenessTimeout"/>, and they flip straight back online the moment they report again.
/// <para>
/// Scoped to the native adapter, which heartbeats every ~30s. Other adapters own their liveness: Zigbee
/// via the bridge-down sweep (see <see cref="EventInterceptor"/>) plus zigbee2mqtt availability — and its
/// battery devices are legitimately silent for long stretches, so a blanket timeout must not expire them.
/// </para>
/// </summary>
public sealed class DeviceLivenessWatchdog : BackgroundService
{
    private const string CapabilityCollection = "capability_devices";

    /// <summary>
    /// How long a native device may go unheard-from before it is marked offline. Must comfortably exceed
    /// the device/emulator heartbeat cadence (~30s, see <c>EmulatorEngine.SimulateLoopAsync</c>) so a
    /// couple of missed beats never false-offline a live device — three missed beats here.
    /// </summary>
    private static readonly TimeSpan LivenessTimeout = TimeSpan.FromSeconds(90);

    /// <summary>How often the staleness sweep runs.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly IMongoDatabase _database;
    private readonly ILogger<DeviceLivenessWatchdog> _logger;

    public DeviceLivenessWatchdog(IMongoDatabase database, ILogger<DeviceLivenessWatchdog> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Grace period before the first sweep: after a (re)start, give every live device a full timeout
        // window to heartbeat at least once, so a cold start after downtime doesn't briefly offline the
        // whole house before the first beats land. Dead rows just wait one extra window to be reconciled.
        try { await Task.Delay(LivenessTimeout, stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation(
            "Device liveness watchdog started (native, timeout {Timeout}s, sweep {Sweep}s)",
            (int)LivenessTimeout.TotalSeconds, (int)SweepInterval.TotalSeconds);

        var collection = _database.GetCollection<CapabilityDeviceDocument>(CapabilityCollection);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = DateTime.UtcNow - LivenessTimeout;
                var filter =
                    Builders<CapabilityDeviceDocument>.Filter.Eq(x => x.AdapterSource, NativeProtocol.AdapterSource)
                    & Builders<CapabilityDeviceDocument>.Filter.Eq(x => x.IsOnline, true)
                    & Builders<CapabilityDeviceDocument>.Filter.Lt(x => x.LastUpdated, cutoff);
                // Flip only the availability flag — LastUpdated stays the true "last heard from" time so the
                // row isn't re-swept every pass and the device re-onlines the instant it next reports.
                var update = Builders<CapabilityDeviceDocument>.Update.Set(x => x.IsOnline, false);

                var result = await collection.UpdateManyAsync(filter, update, cancellationToken: stoppingToken);
                if (result.ModifiedCount > 0)
                    _logger.LogInformation(
                        "Liveness watchdog marked {Count} silent native device(s) offline (no contact for >{Timeout}s)",
                        result.ModifiedCount, (int)LivenessTimeout.TotalSeconds);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during device liveness watchdog sweep");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
