# Схема данных и хранение Domovoy

> Актуально на 2026-07-19 (после Фазы 2 + начала Фазы 3). Описывает реальное состояние кода в `src/`.
> Предыдущая версия документа датировалась 2026-05-31 (эпоха «только `capability_devices`, P0-5/1B ещё не
> реализованы») и была снята как сильно устаревшая — с тех пор появилось более 20 новых коллекций.
>
> Связанные: [`roadmap.md`](roadmap.md) (эпики и DoD), [`positioning_ru.md`](positioning_ru.md)
> (зачем event-log как feature store), [`../runbook.md`](../runbook.md) (запуск стека),
> [`../../memory-bank/currentState.md`](../../memory-bank/currentState.md) (снимок кода целиком).

## Обзор

Хранилище — **только MongoDB**. Доступ к БД инкапсулирован в **DbGateway** (Gateway Pattern): остальные
сервисы не ходят в Mongo напрямую (единственное исключение — Serilog Mongo-sink `ops_logs`, пишущий
напрямую из всех сервисов). Внутри DbGateway **repository-слоя нет** — эндпоинты/сервисы вызывают
`IMongoDatabase.GetCollection<T>(...)` напрямую (26 файлов / ~253 точки использования; самый сцепленный —
`HistoryEndpoints`, роллапы). Это осознанный технический долг — план по введению доменных
store-интерфейсов (шов Mongo↔PostgreSQL) задокументирован как **Эпик 3H** в roadmap.md (пока ADR-уровень,
код не начат). RabbitMQ закрывает и шину сообщений, и MQTT-брокер; мониторинг — Prometheus.

## Поток данных (как состояние попадает в БД)

```
Адаптер (Connectivity)                DbGateway                 ApiGateway            WebUI
  Zigbee2MqttAdapter / DomovoyNativeAdapter / EspHomeMqttAdapter
        │ DeviceDiscoveredV1
        │ DeviceStateReportV1   ─────► EventInterceptor
        │ DeviceOnlineChangedV1         upsert/delta в Mongo
        │  (шина domovoy.*)             capability_devices + device_events + sensor_readings
        ▼
   RabbitMQ (bus + MQTT)                     │
                                             │  GET /api/capability-devices ◄── CapabilityDevicesController
                                             └──────────────────────────────────────────────► список устройств
   EventRelayService (ApiGateway) ── SignalR DeviceHub ──────────────────────────────────────► live-состояние

Команда:  WebUI ─► POST /api/device-control/{id}/set ─► DeviceCommandV1 (domovoy.commands)
          ─► адаптер кодирует в протокол устройства ─► устройство
          (тот же путь используют: control-block actuation, правила, сцены [3B, НЕ закоммичено])
```

Ключевой принцип не изменился: на шине ходят **нормализованные capability-значения**, а не сырые
протокольные payload'ы; кодирование/декодирование живёт в адаптере.

## Активные коллекции (домовая логика)

### `capability_devices` — read-модель устройств
Пишется `EventInterceptor` из контракта (`DeviceDiscoveredV1`/`DeviceStateReportV1`/`DeviceOnlineChangedV1`),
читается WebUI. Модель — [CapabilityDeviceDocument](../../src/Gateway/Domovoy.DbGateway/Models/CapabilityDeviceDocument.cs):
`Id` (GUID, `DeviceIdFactory`), `Name`, `AdapterSource` (`Zigbee2Mqtt`/`DomovoyNative`/`EspHome`/`System`),
`Model?`, `ZoneId`, `Archetype` (2D, авто+override), `Capabilities[]` (`Id`/`Kind`/`Writable`/`Unit?`/`Min?`/`Max?`),
`State: Dictionary<string,object>`, `IsOnline`, `LastUpdated`.

### `device_events` / `sensor_readings` — Mongo time-series feature store (P0-5, 1B)
Append-only. `device_events` ([DeviceEventLog](../../src/Gateway/Domovoy.DbGateway/Models/DeviceEventLog.cs)) —
дельта `oldValue→newValue` + `triggerSource` (user/rule/device/ml/block) + `ruleId`/`decisionId` + `mode`
(1G) + зона; `sensor_readings` ([SensorReading](../../src/Gateway/Domovoy.DbGateway/Models/SensorReading.cs)) —
числовая телеметрия. TTL + индексы через `TimeSeriesInitializer` (`collMod`); агрегации `$dateTrunc`
(minute/hour/day) + батч-эндпоинт (`aggregate/batch`, dashboard-fill); CSV-экспорт.

### `zones` — граф зон (P0-3)
[Zone](../../src/Gateway/Domovoy.DbGateway/Models/Zone.cs): `Id`, `Name`, `ParentZoneId?`, `Kind`. CRUD +
привязка устройства (`PUT /capability-devices/{id}/zone`).

### `automations` / `auto_history` — правила (1A) + история срабатываний
[AutomationRule](../../src/Common/Domovoy.Contracts/Automations/AutomationRule.cs): `Triggers[]` (OR),
`Conditions[]` (AND), `Actions[]` (по порядку, включая delay/notify/**scene** — 3B, НЕ закоммичено),
`Status` (Active/Shadow/Disabled). `AutoHistory` — что сработало, реплей поверх той же модели (1F).

### `control_blocks` / `block_state` / `block_history` — control blocks (1H/1D/2Q)
[ControlBlock](../../src/Common/Domovoy.Contracts/Blocks/ControlBlock.cs): `TypeId`, `Params`, `Options`
(2Q — enum/bool/text), `Outputs[]` (актуация), для композитов — `CompositeDefinition`/`CompositeParam`
(param-passthrough). `block_state` ([BlockStateRecord](../../src/Common/Domovoy.Contracts/Blocks/BlockStateRecord.cs)) —
персистентность stateful-примитивов (on_delay/latch/PID-автотюн/…) между рестартами. `block_history` —
run-records для атрибуции в журнале (`BlockTriggeredV1`).

### `home_state` — текущий режим дома (1G)
[HomeState](../../src/Gateway/Domovoy.DbGateway/Models/HomeState.cs): single-doc, режим (`WellKnownModes`),
публикует `HomeModeChangedV1` при `PUT`.

### `ml_models` / `ml_tasks` — ML-субстрат (2A/2B/2I/2P)
[MlModelDocument](../../src/Gateway/Domovoy.DbGateway/Models/MlModelDocument.cs) — реестр версий (артефакт
inline), стадия (Shadow/Bounded-Active/Full), scorecard/дрифт. [MlTaskDocument](../../src/Gateway/Domovoy.DbGateway/Models/MlTaskDocument.cs) —
runtime-редактируемые задачи обучения (2P): что/на чём/в каких пределах, multi-target `(target, scope)`,
ретенция версий.

### `proposals` — очередь предложений (2C)
[Proposal](../../src/Common/Domovoy.Contracts/Proposals/Proposal.cs): `Kind` (Rule/MlPromotion/MlTask/…),
`Evidence` (локализованное обоснование), `Status` (Pending/Approved/Rejected), `Source` (discovery/ml/…).

### `users` / `roles` — модель ролей (2E, без enforcement)
[User](../../src/Gateway/Domovoy.DbGateway/Models/User.cs) (без пароля/логина), [Role](../../src/Common/Domovoy.Contracts/Security/Role.cs)
(`WellKnownPermissions`, `IsBuiltIn`). Засеяно `SecuritySeeder` (admin/resident/guest, идемпотентно).

### `dashboards` / `dashboard_prefs` — кастомные дашборды
[DashboardDocument](../../src/Gateway/Domovoy.DbGateway/Models/DashboardDocument.cs) — вкладки-конструктор
(пикер+секции, 4+ типа элементов, включая **сцена-плитка** — 3B, НЕ закоммичено); `dashboard_prefs` —
скрытые авто-сферы (по архетипам).

### `site_location` / `calendar_settings` — геолокация + календарь (2K)
[SiteLocation](../../src/Common/Domovoy.Contracts/Home/SiteLocation.cs) (координаты, offline-таймзона
через GeoTimeZone), [CalendarSettings](../../src/Common/Domovoy.Contracts/Home/CalendarSettings.cs)
(праздники, Nager.Date импорт). Питают вирт. сенсоры Sun/Time/Calendar (2L, `SystemSensorService`,
`AdapterSource=System`, не персистятся как отдельная коллекция — публикуются как обычные capability-устройства).

### `narrative_entities` / `narrative_state` / `home_story` — Дневник дома (2N)
[NarrativeEntity](../../src/Common/Domovoy.Contracts/Narrative/NarrativeEntity.cs) (имена духа/жильцов,
override), `narrative_state` ([NarrativeState](../../src/Common/Domovoy.Contracts/Narrative/NarrativeState.cs)) —
ротация тегов/tier-затухание, `home_story` ([HomeStoryEntry](../../src/Common/Domovoy.Contracts/Narrative/HomeStoryEntry.cs)) —
материализованные записи дневника (детерминированный NLG, `Domovoy.Narrative`, без LLM).

### `ops_logs` — операционные логи (Serilog Mongo-sink, 2G)
Capped-коллекция, пишется **напрямую из всех сервисов** (`SerilogBootstrap`, `Serilog.Sinks.MongoDB` 5.4 —
не 7.x). Смешивается с `device_events`/`auto_history` только на чтение (`GET /api/activity`), не на запись —
доменные данные и операционная диагностика физически разделены.

## Коллекции в рабочем дереве (НЕ закоммичено, 2026-07-19)

### `scenes` — сцены как first-class объект (Эпик 3B)
[Scene](../../src/Common/Domovoy.Contracts/Scenes/Scene.cs): `Targets[]` (device+capability-набор,
нормализация JsonElement как у правил). Активация — `POST /api/scenes/{id}/activate` (fan-out
`DeviceCommandV1`, `source=scene:{id}`). Редактирование значений **без** активации (Re-Capture — снимок
текущего writable-состояния в сцену).

### `backup_settings` — бэкапы/восстановление (Эпик 3A)
[BackupSettings](../../src/Gateway/Domovoy.DbGateway/Models/BackupSettings.cs): расписание (таймзона
площадки 2K) + ретенция keep-N. Сам бэкап — не коллекция, а zip-бандл (`manifest.json`+`collections/*.bson`
всех перечисленных выше коллекций через Mongo-драйвер напрямую, без `mongodump`), плюс настройки плагинов
(`.settings/{id}.json`). См. [BackupManifest](../../src/Gateway/Domovoy.DbGateway/Models/BackupManifest.cs),
[`../backup_restore_ru.md`](../backup_restore_ru.md).

## Контракт на шине (источник записей)

Определён в `Domovoy.Contracts` (zero-dep). Конверт — `Envelope<T>` (CloudEvents-стиль). Топология —
[BusTopology](../../src/Common/Domovoy.Contracts/Messaging/BusTopology.cs): exchanges
`domovoy.discovery`/`domovoy.commands`/`domovoy.events`/`domovoy.state` (+ **[НЕ закоммичено]**
`system.control` для self-restart, `SystemControl.cs` в `Domovoy.MessageBus`); версионируемые типы
`domovoy.device.{discovered|state|command|online}.v1`.

## Доступ к данным и эволюция схемы

- Сервисы **не** ходят в Mongo напрямую — только через DbGateway (REST) или события шины (кроме
  Serilog-sink, см. выше).
- Схема эволюционирует гибко (Mongo, document-friendly типы); ломающие изменения контракта шины — через
  новый `.vN` суффикс (старые/новые консьюмеры сосуществуют).
- **Куда движемся:** Эпик 3H (roadmap.md) вводит доменные store-интерфейсы внутри DbGateway как шов для
  альтернативного бэкенда (PostgreSQL — JSONB+`date_trunc`, НЕ TimescaleDB); Mongo остаётся default.

## Производительность

- Индексы по часто запрашиваемым полям (`_id`/`DeviceId`, `ZoneId`, `Timestamp` для time-series).
- Состояние хранится как словарь capability→значение (гибкость без миграций схемы).
- Телеметрия (1B): Mongo time-series, ретеншн TTL, минутные/часовые/дневные свёртки, батч-агрегация
  (dashboard-fill убивает N+1 для множества плиток/графиков одним запросом), CSV-экспорт.
