// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.DbGateway.Config;

/// <summary>
/// Telemetry data-platform options (roadmap Epic 1B). Bound from the <c>Telemetry</c> config section.
/// </summary>
public class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>
    /// Retention for <b>raw</b> <c>sensor_readings</c> samples, in days. <c>0</c> (default) keeps them
    /// forever. The domain event-log (<c>device_events</c>) is the replayable feature store and is
    /// deliberately <i>never</i> expired here — only raw numeric telemetry is subject to retention, since
    /// aggregated history is served on the fly from it (so old raw samples can be dropped without losing
    /// the ability to chart trends within the retained window).
    /// </summary>
    public int RawRetentionDays { get; set; } = 0;
}
