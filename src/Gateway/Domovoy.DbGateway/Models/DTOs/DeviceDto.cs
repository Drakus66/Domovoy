namespace Domovoy.DbGateway.Models.DTOs;

public record DeviceDto(
    string? Id,
    string Name,
    string Type,
    string Location,
    string State,
    bool IsOnline,
    DateTime LastSeen,
    Dictionary<string, object> Configuration
);

public record CreateDeviceDto(
    string Name,
    string Type,
    string Location,
    Dictionary<string, object>? Configuration = null
);

public record UpdateDeviceDto(
    string Name,
    string Type,
    string Location,
    Dictionary<string, object>? Configuration = null
);

public record UpdateDeviceStateDto(
    string State
);
