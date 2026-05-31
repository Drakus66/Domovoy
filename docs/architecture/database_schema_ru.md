# Схема данных и хранение Domovoy

> Актуально на 2026-05-31 (после Шага 5 capability-миграции). Описывает реальное состояние кода в
> `src/`. Прошлая версия документа (legacy `Device`/`Light`/`Sensor`/`MqttDevice`, Redis, PostgreSQL)
> относилась к снятой 6-сервисной архитектуре и удалена.
>
> Связанные: [`roadmap.md`](roadmap.md) (планы по data-path: P0-4, P0-5, 1B), [`positioning_ru.md`](positioning_ru.md)
> (зачем event-log как feature store), [`../runbook.md`](../runbook.md) (запуск стека).

## Обзор

Хранилище — **только MongoDB** (текущее состояние + будущие time-series). Доступ к БД
инкапсулирован в **DbGateway** (Gateway Pattern): остальные сервисы не ходят в Mongo напрямую, а
получают данные через REST DbGateway или реагируют на события шины. Redis и PostgreSQL в системе
**нет**; RabbitMQ закрывает и шину сообщений, и MQTT-брокер; мониторинг — Prometheus.

## Поток данных (как состояние попадает в БД)

```
Адаптер (Connectivity)                DbGateway                 ApiGateway            WebUI
  Zigbee2MqttAdapter / NativeAdapter
        │ DeviceDiscoveredV1
        │ DeviceStateReportV1   ─────► EventInterceptor
        │ DeviceOnlineChangedV1         upsert в Mongo
        │  (шина domovoy.*)             коллекция
        ▼                               capability_devices
   RabbitMQ (bus + MQTT)                     │
                                             │  GET /api/capability-devices ◄── CapabilityDevicesController
                                             └──────────────────────────────────────────────► список устройств
   EventRelayService (ApiGateway) ── SignalR DeviceHub ──────────────────────────────────────► live-состояние

Команда:  WebUI ─► POST /api/device-control/{id}/set ─► DeviceCommandV1 (domovoy.commands)
          ─► адаптер кодирует в протокол устройства ─► устройство
```

Ключевой принцип: на шине ходят **нормализованные capability-значения** (`on_off`=bool,
`brightness`=0..100), а не сырые протокольные payload'ы. Кодирование/декодирование живёт в адаптере
(`Zigbee2MqttCodec`), нативный протокол — near-identity.

## Активные коллекции

### `capability_devices` — read-модель устройств (используется)

Пишется `EventInterceptor` из контракта (`DeviceDiscoveredV1` / `DeviceStateReportV1` /
`DeviceOnlineChangedV1`), читается WebUI. Модель — [CapabilityDeviceDocument](../../src/Gateway/Domovoy.DbGateway/Models/CapabilityDeviceDocument.cs).

| Поле | Тип | Назначение |
|---|---|---|
| `Id` (`_id`) | string (GUID) | Логический id устройства, стабильный при переименовании/рестарте (`DeviceIdFactory`) |
| `Name` | string | Отображаемое имя |
| `AdapterSource` | string | Владелец-адаптер: `Zigbee2Mqtt` / `DomovoyNative` |
| `Model` | string? | Модель устройства |
| `ZoneId` | string | Зона (привязка к участку/комнате; см. P0-3) |
| `Capabilities` | `CapabilityDocument[]` | Список возможностей (см. ниже) |
| `State` | `Dictionary<string,object>` | Последнее нормализованное состояние по каждой capability |
| `IsOnline` | bool | Доступность (из `DeviceOnlineChangedV1`) |
| `LastUpdated` | DateTime (UTC) | Время последнего обновления |

`CapabilityDocument` (плоский дескриптор возможности): `Id`, `Kind` (`Boolean`/`Number`/`Enum`/`Color`/`Text`/`Action`),
`Writable`, `Unit?`, `Min?`, `Max?`. Типы BSON-дружественные (без contract-записей) — простая сериализация Mongo.

## Определённые, но ещё не подключённые модели

Существуют в `src/Gateway/Domovoy.DbGateway/Models/`, но запись/использование появятся по roadmap —
документируются как фундамент, чтобы не вводить дубль-модели позже:

| Модель | Файл | Статус / когда оживёт |
|---|---|---|
| `SensorReading` | [SensorReading.cs](../../src/Gateway/Domovoy.DbGateway/Models/SensorReading.cs) | Модель есть, записи нет → **P0-5 / Эпик 1B** (Mongo time-series телеметрия) |
| `Automation` | [Automation.cs](../../src/Gateway/Domovoy.DbGateway/Models/Automation.cs) | `trigger`/`condition`/`action` как словари → **Эпик 1A** (AutomationService) |
| `AutoHistory` | [AutoHistory.cs](../../src/Gateway/Domovoy.DbGateway/Models/AutoHistory.cs) | История срабатываний → **1A** + объяснимость/реплей **1F** |
| `Location` | [Location.cs](../../src/Gateway/Domovoy.DbGateway/Models/Location.cs) | Граф зон (`ParentLocationId`, `Floor`) не используется → **P0-3** (зоны first-class) |
| `User` / `UserAccess` | Models/ | Авторизация отложена → **Фаза 2** (локальная auth) |

## Доменный event-log (планируется — P0-5)

> ⚠️ Это «**пока не поздно**»-решение. Подробные требования к схеме — в [`roadmap.md`](roadmap.md), Эпик P0-5.

Append-only журнал в **Mongo time-series collection** (та же БД). Схема фиксируется сразу как
**реплейабельный feature store**: каждая запись несёт `timestamp`, `zone`, `deviceId`, `capabilityId`,
**`oldValue → newValue`** (дельта, не только новое), **`triggerSource`** (пользователь/правило/адаптер/ML),
**`ruleId`/`decisionId`** (связка с `AutoHistory` и трассировкой), **`mode`/`context`**. Это топливо
ML (Фаза 2) и основа объяснимости/реплея (Эпик 1F). Доменный event-log — это *данные*; операционные
логи (Serilog) — отдельно, не смешивать.

## Контракт на шине (источник записей)

Определён в `Domovoy.Contracts` (zero-dep). Конверт — `Envelope<T>` (CloudEvents-стиль). Топология —
[BusTopology](../../src/Common/Domovoy.Contracts/Messaging/BusTopology.cs):

- **Exchanges** (AMQP topic): `domovoy.discovery`, `domovoy.commands`, `domovoy.events`, `domovoy.state`.
- **Routing keys**: `device.discovered`, `device.command`, `device.state.updated`, `device.online.changed`.
- **Версионируемые типы**: `domovoy.device.{discovered|state|command|online}.v1`.
- **Payloads** ([Payloads.cs](../../src/Common/Domovoy.Contracts/Messaging/Payloads.cs)):
  `DeviceDiscoveredV1(DeviceDescriptor)`, `DeviceStateReportV1(DeviceId, State)`,
  `DeviceCommandV1(DeviceId, Set)`, `DeviceOnlineChangedV1(DeviceId, IsOnline)`.

## Доступ к данным и эволюция схемы

- Сервисы **не** ходят в Mongo напрямую — только через DbGateway (REST) или события шины.
- DbGateway: Minimal API, эндпоинты [CapabilityDeviceEndpoints](../../src/Gateway/Domovoy.DbGateway/Endpoints/CapabilityDeviceEndpoints.cs)
  (`GET /api/capability-devices`, `GET /api/capability-devices/{id}`); запись — `EventInterceptor`.
  ApiGateway проксирует чтения через `CapabilityDevicesController` (Ocelot убран — единый endpoint routing).
- Схема эволюционирует гибко (Mongo, document-friendly типы); ломающие изменения контракта шины —
  через новый `.vN` суффикс (старые/новые консьюмеры сосуществуют).

## Производительность

- Индексы по часто запрашиваемым полям (`_id`/`DeviceId`, `ZoneId`, `Timestamp` для time-series).
- Состояние хранится как словарь capability→значение (гибкость без миграций схемы).
- Для телеметрии (P0-5/1B): Mongo time-series, ретеншн-политики, минутные/часовые свёртки, экспорт за период.
