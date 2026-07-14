// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Common.Models.Events
{
    /// <summary>
    /// Event class for device state updates
    /// </summary>
    public class DeviceStateUpdatedEvent : BaseEvent
    {
        public string DeviceId { get; set; } = null!;
        public Dictionary<string, object> State { get; set; } = new();
    }
}
