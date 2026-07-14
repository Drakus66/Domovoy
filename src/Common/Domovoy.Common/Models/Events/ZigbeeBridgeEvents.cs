// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Common.Models.Events;

public class ZigbeeBridgeStateEvent : BaseEvent
{
    public bool IsOnline { get; set; }
}

public class ZigbeeBridgeInfoEvent : BaseEvent
{
    public string Version { get; set; } = string.Empty;
    public string CoordinatorType { get; set; } = string.Empty;
    public string CoordinatorAddress { get; set; } = string.Empty;
    public int Channel { get; set; }
    public int PanId { get; set; }
    public bool PermitJoin { get; set; }
    public int PermitJoinTimeout { get; set; }
}

public class ZigbeeNetworkEvent : BaseEvent
{
    public string EventType { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string IeeeAddress { get; set; } = string.Empty;
}
