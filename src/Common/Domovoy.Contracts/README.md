# Domovoy.Contracts

Единый версионируемый контракт системы (roadmap **P0-1 / P0-2**). От него зависят сервисы,
плагины и ML — но он **не зависит ни от чего** (чистые POCO/record, без NuGet-пакетов и без
`Domovoy.Common`), чтобы к нему мог привязаться любой процесс, включая полиглот-плагины.

## Содержимое

| Область | Тип | Назначение |
|---|---|---|
| `Capabilities/` | `Capability`, `CapabilityKind`, `CapabilityIds`, `CapabilityAttributeKeys`, `WellKnownCapabilities` | Открытая capability-модель: устройство = набор возможностей, а не закрытый enum-тип |
| `Devices/` | `DeviceDescriptor`, `DeviceIdentity`, `CapabilityState` | Каноническое описание устройства и его нормализованного состояния (по capability id) |
| `Messaging/` | `Envelope` / `Envelope<T>`, `MessageTypes`, `BusTopology`, payload-контракты `*V1` | CloudEvents-конверт, версионируемые типы, единое именование шины |

## Принципы

- **Открытая модель.** `Capability(id, kind, attrs)` + `CapabilityState` (словарь capability id → value
  с типизированными аксессорами). Новая возможность / новый плагин — без перекомпиляции ядра.
- **Версионирование.** Тип сообщения несёт суффикс `.vN` (`domovoy.device.state.v1`); ломающее
  изменение = новый суффикс, старые и новые потребители сосуществуют на время миграции.
- **Одно именование шины.** `BusTopology` — единственный источник правды по exchange/routing key
  (дотти-конвенция), заменяет разнобой `domovoy/discovery` ↔ `device.commands` ↔ `domovoy.state`.

## Нормализация (почему это важно)

Адаптер (Connectivity) обязан **декодировать** протокол-native payload в `CapabilityState`
(напр. Z2M `{state:"ON",brightness:254}` → `on_off=true, brightness=100`) и **кодировать** команду
(`brightness=50` → `{brightness:127}`). Домен/UI/автоматизации/ML видят только нормализованный
контракт — это устраняет текущие баги несоответствия ключей/форматов между Z2M и доменными моделями.

## Статус миграции (аддитивно + инкрементально)

1. ✅ Контракт создан (этот проект) + `DeviceIdFactory` (детерминированные id).
2. ✅ Z2M-адаптер публикует новый контракт **аддитивно** (рядом со старым путём): `definition.exposes`
   → `Capability[]` + `Zigbee2MqttCodec` → `DeviceDiscoveredV1` / `DeviceStateReportV1` на `BusTopology`.
   Codec.Encode (команда→z2m) готов, но подключится к `HandleCommandAsync` на Шаге 3 (когда менеджер
   начнёт слать `DeviceCommandV1`). TODO codec: color (xy/hs), multi-gang (state_l1/l2), cover/valve.
3. ✅ Потребитель контракта: новый `CapabilityDeviceManager` (UnifiedDeviceService) потребляет
   `DeviceDiscoveredV1`/`DeviceStateReportV1`, ведёт capability-реестр и переизлучает нормализованное
   состояние как `DeviceStateUpdatedEvent` (SignalR/EventInterceptor работают). Адаптер принимает
   `DeviceCommandV1` и кодирует команду в MQTT через `Codec.Encode`. Работает **параллельно** со старым
   `UnifiedDeviceManager`. Старта-гидрация из БД не требуется: id детерминированы, дубли при рестарте
   невозможны, реестр восстанавливается при ре-анонсе адаптера.
4. ✅ DbGateway / WebUI на новый контракт. **Backend:** DbGateway персистит read-модель
   `capability_devices` (EventInterceptor ← `DeviceDiscoveredV1`/`DeviceStateReportV1`/`DeviceOnlineChangedV1`)
   + GET `/api/capability-devices[/{id}]` + Ocelot-маршрут; ApiGateway публикует `DeviceCommandV1`
   (`POST /api/device-control/{id}/set`). **WebUI:** страница `/devices` (список + контролы по
   capability `kind` + команды + live по SignalR), `tsc` зелёный.
5. ✅ Legacy снят полностью: старый device-type путь (UnifiedDeviceManager + IDeviceTypeHandler + handlers + IdentityResolver), legacy `IProtocolAdapter` события и dual-publish, старый `POST /command` + `ZigbeeController.SetDeviceState`, легаси-подписки `EventInterceptor`/`EventRelayService`, Common-модели (`Device`/`Light`/`Sensor`/`MqttDevice`/`BaseEntity`/events/enums/commands/`BaseService`/`IDeviceService`/`OrchestrationCommand`/`AdminController`), DbGateway legacy (`Device`+repo+`MapDeviceEndpoints`) + Ocelot маршруты `/api/devices|locations|sensors`, WebUI legacy (`Dashboard`/`api/devices`/store/types/components), `MessageBusConfiguration` урезан, `RabbitMQConnection.ConfigureMqttExchanges` убран. Все 7 .NET-проектов + `tsc` фронтенда — зелёные.

**Native-трек (DIY/Arduino, ~50% устройств):** определён capability-native протокол **Domovoy.Native v1**
(`Native/NativeProtocol` + `NativeAnnounceV1`, топики `domovoy/native/<id>/{announce,state,set,availability}`,
broadcast `domovoy/native/discover` для пере-анонса после рестарта сервера, и `domovoy/hub/<hubId>/status`
для доступности борда: один MQTT-LWT на соединение → адаптер гасит все устройства хаба при падении борда).
`DomovoyNativeAdapter` мигрирован (near-identity — без codec, значения уже нормализованы) и кормит тот же
`DeviceDiscoveredV1`/`DeviceStateReportV1`/`DeviceOnlineChangedV1`. Эмулятор пересобран на native v1 +
встроенный веб-UI (:5080). ✅ Прошивка `Arduino/libraries/DomovoyClient` приведена к v1 (потоковый announce
без буфера JSON, подписка на `discover`, hub-LWT; настроена под малый SRAM Arduino Nano).

См. [docs/architecture/roadmap.md](../../../docs/architecture/roadmap.md).
