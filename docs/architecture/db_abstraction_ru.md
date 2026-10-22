# Абстракция хранилища: шов store-интерфейсов (Эпик 3H)

> **Решение владельца №8.** Mongo остаётся дефолтом, но продукт должен дать **выбор БД** (как HA recorder:
> MariaDB/PostgreSQL — «пользователи это любят») и застраховаться от платформенных сюрпризов Mongo (ARMv8.2 на
> Pi, AVX на старых x86). Здесь фиксируется **шов** (Ф0–Ф1); сам переход на вторую БД (Ф2) отложен.

## Принцип шва

С Mongo говорит **только DbGateway** (остальные сервисы — HTTP/шина). Внутри DbGateway вводится слой
**доменных store-интерфейсов**: эндпоинты и сервисы зовут не `IMongoDatabase`/`Builders`/`Aggregate`, а
доменные операции. **Железное правило:** наружу интерфейса **не торчат** типы хранилища —
`IQueryable`, `FilterDefinition`, `BsonDocument`. Только простые параметры на вход и plain-records на выход.
Иначе шов фиктивный.

```
Endpoint (HTTP-обвязка)  →  IXxxStore (доменные операции)  →  MongoXxxStore (вся специфика Mongo)
                                        ↑
                              PostgresXxxStore (Ф2, будущая реализация того же контракта)
```

Смена бэкенда = новая реализация интерфейсов + строка в DI; ничего выше шва не меняется. Интеграционные
тесты пишутся **против интерфейса** и параметризуются бэкендом.

## Ф1 — референсная миграция: телеметрия/история (сделано)

Мигрирован **самый сцепленный** домен — feature store (event-log P0-5 + числовая телеметрия 1B + роллапы):

- **`Stores/ITelemetryStore.cs`** — доменные операции: `QueryEventsAsync`/`QueryTelemetryAsync`,
  `Count*`/`Count*ByZone`, `EarliestEventAsync`, `AggregateTelemetryAsync`, `AggregateBatchAsync`,
  `LatestByDeviceAsync`. Доменные records: `EventLogRow`/`TelemetryRow`/`AggregateBucket`/`SeriesSpec`/
  `SeriesResult`/`LatestEventRow`/`ZoneCount` — без единого типа Mongo.
- **`Stores/Mongo/MongoTelemetryStore.cs`** — вся специфика: time-series коллекции (`TimeSeriesInitializer`),
  `$dateTrunc`-роллапы, `$group`-аккумуляторы, reset-aware `delta`, `$or`-батч. `IMongoDatabase` в ctor.
- **`Endpoints/HistoryEndpoints.cs`** — стал **тонким**: парсинг параметров, CSV-экспорт, маппинг
  `ArgumentException`→400. Ни одного `MongoDB.Driver`-типа.
- **`Endpoints/EnergyEndpoints.cs`** — второй потребитель домена: `ConsumptionAsync`/`BreakdownAsync`/
  `CostAsync` теперь берут `ITelemetryStore` вместо прямого вызова Mongo-агрегации.
- **DI:** `AddSingleton<ITelemetryStore, MongoTelemetryStore>()` в `Program.cs`.
- **Тесты через шов:** `HistoryBatchTests`, `EnergyTests`, `PowerTopologyTests` дёргают `ITelemetryStore`/
  `MongoTelemetryStore` (не старые статики) — тот же тест пойдёт против будущего PostgreSQL-store.

## Ф0 — карта доменных store-интерфейсов (проектирование)

Остальные домены DbGateway мигрируют **инкрементально по этому же паттерну** (по одному в PR). Карта:

| Store-интерфейс | Коллекция(и) | Эндпоинт(ы) |
|---|---|---|
| `ITelemetryStore` ✅ | `device_events`, `sensor_readings` | History, Energy |
| `IDeviceStore` | `capability_devices` | CapabilityDevice, Energy(read) |
| `IZoneStore` | `zones` | Zone |
| `IAutomationStore` | `automations` | Automation, Proposals(apply) |
| `IBlockStore` / `IBlockStateStore` | `control_blocks`, `block_state` | Block, BlockState |
| `ISceneStore` | `scenes` | Scene, Proposals(apply) |
| `IProposalStore` | `proposals` | Proposals |
| `IMlModelStore` / `IMlTaskStore` / `IMlActivityStore` | `ml_models`, `ml_tasks`, `ml_activity` | Ml, MlTask, MlActivity |
| `ISettingsStore` | `site_location`, `calendar_settings`, `tariff_settings`, `load_management_settings`, `ml_settings`, `presence_settings`, `notification_settings` | Settings |
| `IResidentStore` | `residents` | Residents |
| `IPowerTopologyStore` | `power_topology` | PowerTopology, Energy |
| `IDashboardStore` | `dashboards`, `dashboard_prefs` | Dashboard |
| `IUserStore` / `IRoleStore` / `IAuthStore` | `users`, `roles` | Users, Roles, Auth |
| `IVariableStore` | `variables` | Variable |
| `IHomeStoryStore` / `INarrativeEntityStore` | `home_story`, `narrative_entities` | HomeStory, NarrativeEntity |
| `IActivityStore` | `ops_logs`, `auto_history`(+ event-log мердж) | Activity |
| `IModeStore` | `home_state` | Mode |

**Serilog Mongo-sink (`ops_logs`)** — единственная сцепка с Mongo вне DbGateway; выбор sink остаётся по
конфигу в `SerilogBootstrap` (переносится на Ф2 вместе с `IActivityStore`).

## Ф2 — коннектор PostgreSQL (ОТЛОЖЕНО)

Лучший «реляционный» кандидат: JSONB ≅ гибкие документы, `date_trunc` ≅ `$dateTrunc` 1:1, ML-артефакты →
`bytea`; TTL/capped → app-level фоновые чистки (без `pg_cron`); `Testcontainers.PostgreSql`. **НЕ
TimescaleDB community** (TSL = source-available → ломает лицензионное правило); обычные таблицы +
BRIN/партиции достаточно для домашнего масштаба. MariaDB — только по спросу (JSON слабее).

## Статус

- **Ф0 (карта + ADR): готово** — принципы, полная карта интерфейсов, план миграции зафиксированы.
- **Ф1 (шов внутри DbGateway): референс готов** — домен телеметрии/истории вырезан за `ITelemetryStore`,
  два эндпоинта (History, Energy) мигрированы, тесты идут через шов, сборка зелёная. **Остаток Ф1:** ~24
  домена из карты выше мигрируются инкрементально по тому же образцу (DoD Ф1 — «ни один эндпоинт/сервис
  DbGateway не использует MongoDB.Driver напрямую» — закрывается по мере прохождения карты).
- **Ф2 (PostgreSQL): не начат** (осознанно отложен владельцем).
