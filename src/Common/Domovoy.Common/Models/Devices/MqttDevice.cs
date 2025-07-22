namespace Domovoy.Common.Models.Devices;

using System.Text.Json.Serialization;

/// <summary>
/// Представляет устройство, подключаемое через протокол MQTT.
/// Содержит метаданные, необходимые для обнаружения, подключения и управления MQTT устройствами.
/// </summary>
public class MqttDevice : Device
{
    /// <summary>
    /// MQTT топик для команд устройству
    /// </summary>
    public string CommandTopic { get; set; } = string.Empty;
    
    /// <summary>
    /// MQTT топик для состояния устройства
    /// </summary>
    public string StateTopic { get; set; } = string.Empty;
    
    /// <summary>
    /// MQTT топик для доступности устройства
    /// </summary>
    public string AvailabilityTopic { get; set; } = string.Empty;
    
    /// <summary>
    /// Строка, обозначающая онлайн-статус устройства
    /// </summary>
    public string PayloadAvailable { get; set; } = "online";
    
    /// <summary>
    /// Строка, обозначающая оффлайн-статус устройства
    /// </summary>
    public string PayloadNotAvailable { get; set; } = "offline";
    
    /// <summary>
    /// Поддерживает ли устройство сохранение последнего состояния (retain flag)
    /// </summary>
    public bool RetainState { get; set; } = true;
    
    /// <summary>
    /// Уровень QoS для коммуникации с устройством
    /// </summary>
    public int QoS { get; set; } = 1;
    
    /// <summary>
    /// MAC адрес устройства (если доступен)
    /// </summary>
    public string MacAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// IP адрес устройства (если доступен)
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// Версия прошивки устройства
    /// </summary>
    public string FirmwareVersion { get; set; } = string.Empty;
    
    /// <summary>
    /// Идентификатор клиента MQTT
    /// </summary>
    public string ClientId { get; set; } = string.Empty;
    
    /// <summary>
    /// Формат данных устройства (JSON, plaintext, etc.)
    /// </summary>
    public string PayloadFormat { get; set; } = "json";
    
    /// <summary>
    /// Время последнего соединения с устройством
    /// </summary>
    public DateTime LastConnected { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Время последнего heartbeat от устройства
    /// </summary>
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Интервал heartbeat в секундах
    /// </summary>
    public int HeartbeatInterval { get; set; } = 60;
    
    /// <summary>
    /// Максимальное количество пропущенных heartbeat до признания устройства отключенным
    /// </summary>
    public int MaxMissedHeartbeats { get; set; } = 3;
    
    /// <summary>
    /// Была ли выполнена автоматическая конфигурация устройства
    /// </summary>
    public bool IsConfigured { get; set; } = false;
    
    /// <summary>
    /// Конфигурация устройства в формате JSON
    /// </summary>
    public string Configuration { get; set; } = string.Empty;
    
    /// <summary>
    /// Дополнительные топики для конкретных функций устройства
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> AdditionalTopics { get; set; } = new Dictionary<string, string>();
    
    /// <summary>
    /// Сериализованная версия AdditionalTopics для хранения в базе данных
    /// </summary>
    public string SerializedTopics
    {
        get => string.Join(";", AdditionalTopics.Select(t => $"{t.Key}={t.Value}"));
        set
        {
            AdditionalTopics.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                foreach (var item in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = item.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        AdditionalTopics[parts[0]] = parts[1];
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Поддерживает ли устройство формат Home Assistant MQTT Discovery
    /// </summary>
    public bool SupportsHomeAssistantDiscovery { get; set; } = false;
    
    /// <summary>
    /// Идентификатор в формате Home Assistant (используется для discovery)
    /// </summary>
    public string HomeAssistantEntityId { get; set; } = string.Empty;
    
    /// <summary>
    /// Тип entity в формате Home Assistant
    /// </summary>
    public string HomeAssistantEntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Конструктор по умолчанию
    /// </summary>
    public MqttDevice()
    {
        Type = Domovoy.Common.Models.Enums.EntityTypes.GlobalEntityTypes.Generic; // Используем Generic для MQTT устройства
    }
    
    /// <summary>
    /// Возвращает стандартный топик для команд устройства
    /// </summary>
    public static string GetDefaultCommandTopic(string deviceId) => $"domovoy/{deviceId}/command";
    
    /// <summary>
    /// Возвращает стандартный топик для состояния устройства
    /// </summary>
    public static string GetDefaultStateTopic(string deviceId) => $"domovoy/{deviceId}/state";
    
    /// <summary>
    /// Возвращает стандартный топик для доступности устройства
    /// </summary>
    public static string GetDefaultAvailabilityTopic(string deviceId) => $"domovoy/{deviceId}/availability";
}
