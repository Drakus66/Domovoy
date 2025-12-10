namespace Domovoy.Common.Models.Events;

using Domovoy.Common.Models.Enums.EntityTypes;

/// <summary>
/// События для обнаружения и регистрации устройств
/// </summary>

/// <summary>
/// Событие оповещения об устройстве (Device Announcement)
/// </summary>
public class DeviceAnnouncementEvent : BaseEvent
{
    /// <summary>
    /// Идентификатор устройства
    /// </summary>
    public Guid DeviceId { get; set; }
    
    /// <summary>
    /// Тип устройства
    /// </summary>
    public string DeviceType { get; set; } = string.Empty;
    
    /// <summary>
    /// Название устройства
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Модель устройства
    /// </summary>
    public string Model { get; set; } = string.Empty;
    
    /// <summary>
    /// Производитель устройства
    /// </summary>
    public string Manufacturer { get; set; } = string.Empty;
    
    /// <summary>
    /// Версия ПО устройства
    /// </summary>
    public string FirmwareVersion { get; set; } = string.Empty;
    
    /// <summary>
    /// MAC-адрес устройства (если доступен)
    /// </summary>
    public string MacAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// IP-адрес устройства (если доступен)
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// Топик для команд устройству
    /// </summary>
    public string CommandTopic { get; set; } = string.Empty;
    
    /// <summary>
    /// Топик для состояния устройства
    /// </summary>
    public string StateTopic { get; set; } = string.Empty;
    
    /// <summary>
    /// Топик для доступности устройства
    /// </summary>
    public string AvailabilityTopic { get; set; } = string.Empty;
    
    /// <summary>
    /// Дополнительные топики устройства
    /// </summary>
    public Dictionary<string, string> AdditionalTopics { get; set; } = new Dictionary<string, string>();
    
    /// <summary>
    /// Поддерживаемые функции устройства
    /// </summary>
    public List<string> Features { get; set; } = new List<string>();
    
    /// <summary>
    /// Поддерживает ли устройство формат Home Assistant Discovery
    /// </summary>
    public bool SupportsHomeAssistant { get; set; } = false;
    
    /// <summary>
    /// Дополнительные данные об устройстве
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}

/// <summary>
/// Событие статуса устройства
/// </summary>
public class DeviceStatusEvent : BaseEvent
{
    /// <summary>
    /// Идентификатор устройства
    /// </summary>
    public Guid DeviceId { get; set; }
    
    /// <summary>
    /// Статус устройства (online/offline)
    /// </summary>
    public string Status { get; set; } = string.Empty;
    
    /// <summary>
    /// Время последнего обновления статуса
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Причина изменения статуса (если известна)
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Событие обнаружения устройства в формате Home Assistant
/// </summary>
public class HomeAssistantDiscoveryEvent : BaseEvent
{
    /// <summary>
    /// Компонент Home Assistant (sensor, light, switch, и т.д.)
    /// </summary>
    public string Component { get; set; } = string.Empty;
    
    /// <summary>
    /// Идентификатор узла (node_id или уникальный идентификатор)
    /// </summary>
    public string NodeId { get; set; } = string.Empty;
    
    /// <summary>
    /// Идентификатор объекта (object_id)
    /// </summary>
    public string ObjectId { get; set; } = string.Empty;
    
    /// <summary>
    /// Полная конфигурация устройства в формате Home Assistant
    /// </summary>
    public Dictionary<string, object> Config { get; set; } = new Dictionary<string, object>();
}

/// <summary>
/// Событие обнаружения нового устройства
/// </summary>
public class DeviceDiscoveredEvent : BaseEvent
{
    /// <summary>
    /// Идентификатор устройства
    /// </summary>
    public Guid DeviceId { get; set; }
    
    /// <summary>
    /// Тип устройства
    /// </summary>
    public GlobalEntityTypes DeviceType { get; set; }
    
    /// <summary>
    /// Название устройства
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Состояние устройства
    /// </summary>
    public Dictionary<string, object> State { get; set; } = new Dictionary<string, object>();
    
    /// <summary>
    /// Метаданные устройства
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}
