// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Common.Models.Commands;

public enum ZigbeeBridgeCommandType
{
    PermitJoin,
    RenameDevice,
    RemoveDevice,
}

public class ZigbeeBridgeCommand : BaseCommand
{
    public ZigbeeBridgeCommandType CommandType { get; set; }
    public int PermitJoinDuration { get; set; } = 254;
    public string TargetDevice { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
}
