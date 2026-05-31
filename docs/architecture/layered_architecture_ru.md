# Многоуровневая архитектура Domovoy

> Актуально на 2026-05-31 (после Шага 5 capability-миграции). Описывает реальный состав сервисов в
> `src/`. Прошлая версия (6 сервисов: DeviceService/LightService/SensorService/DiscoveryService)
> относилась к снятой архитектуре и удалена.
>
> Связанные: [`roadmap.md`](roadmap.md), [`positioning_ru.md`](positioning_ru.md),
> [`database_schema_ru.md`](database_schema_ru.md), [`architecture_diagrams_ru.md`](architecture_diagrams_ru.md),
> [`../runbook.md`](../runbook.md).

## Обзор

Domovoy — событийно-ориентированная (event-driven) система на .NET 9: адаптеры протоколов
нормализуют устройства в **capability-модель**, общение между сервисами идёт через **RabbitMQ**
(шина + MQTT-брокер) единым версионируемым контрактом `Domovoy.Contracts`. После consolidation +
capability-миграции состав — **2 backend-сервиса + 2 шлюза + контракт + UI**. Хранилище — MongoDB,
мониторинг — Prometheus. Ocelot, Redis, PostgreSQL, Grafana/Loki убраны.

## Состав компонентов

### Контракт (фундамент, zero-dep)
- **`Domovoy.Contracts`** — `Envelope<T>` (CloudEvents), открытая capability-модель (`Capability`,
  `CapabilityState`, `DeviceDescriptor`), `BusTopology` (единое именование шины), `DeviceIdFactory`
  (детерминированные id), `Native` (протокол Domovoy.Native v1). От него зависят все сервисы и
  будущие плагины — привязка к версионируемой схеме, а не к внутренним классам.

### Backend-сервисы
- **`Domovoy.Connectivity`** — единственный MQTT-клиент + адаптеры протоколов (`AdapterManager`):
  - `Zigbee2MqttAdapter` (+ `Zigbee2MqttCodec`): `exposes` → capabilities, decode/encode сырых
    Z2M-значений ↔ нормализованных;
  - `DomovoyNativeAdapter`: near-identity для DIY-устройств (Domovoy.Native v1), без кодека;
  - `ZigbeeBridgeCache` — состояние Zigbee-моста.
  Публикует `DeviceDiscoveredV1`/`DeviceStateReportV1`/`DeviceOnlineChangedV1`, исполняет `DeviceCommandV1`.
- **`Domovoy.UnifiedDeviceService`** — `CapabilityDeviceManager` (BackgroundService): подписан на
  capability-контракт (`domovoy.discovery`/`domovoy.state`), переизлучает нормализованное состояние
  для SignalR/персиста.

### Шлюзы
- **`Domovoy.DbGateway`** — Minimal API + Mongo. `EventInterceptor` персистит read-модель
  `capability_devices`; эндпоинты `GET /api/capability-devices[/{id}]`. Единая точка доступа к БД.
- **`Domovoy.ApiGateway`** — точка входа клиентов + SignalR. `DeviceControlController`
  (`POST /api/device-control/{id}/set` → `DeviceCommandV1`), `CapabilityDevicesController`
  (проксирует чтения в DbGateway), `ZigbeeController` (управление мостом), `StatusController`/
  `MetricsController`; `EventRelayService` + `DeviceHub` (SignalR) транслируют состояние в UI.
  Маршрутизация — встроенный endpoint routing (Ocelot убран).

### Общие и инфраструктурные
- **`Domovoy.Common`** — общий код (`IMessageBus`, `MessageBusConfiguration`, события моста,
  `SerilogBootstrap`). **Внимание:** часть legacy-моделей здесь помечена к удалению (Шаг 5) — новый
  код должен зависеть от `Domovoy.Contracts`, а не от `Common.Models.*`.
- **`Domovoy.MessageBus`** — RabbitMQ-подключение (`RabbitMQConnection`), ленивая декларация exchange'ей.
- **WebUI** — индекс = страница `/devices` (список из `GET /api/capability-devices`, контролы по
  `kind` каждой capability, команды через `POST /api/device-control/{id}/set`, live по SignalR).
- **`Domovoy.DeviceEmulator`** (`src/Tools/`) — dev-инструмент: виртуальные capability-устройства
  на Domovoy.Native v1 + встроенный веб-UI (:5080). Не в Docker (намеренно).

## Уровни (логические, внутри event-driven backbone)

1. **Представление:** WebUI + ApiGateway (REST вход, SignalR-выход).
2. **Координация/домен:** UnifiedDeviceService (`CapabilityDeviceManager`) — нормализация и
   переизлучение состояния. Здесь же в Фазе 1 появится `AutomationService` (правила).
3. **Подключение устройств:** Connectivity + адаптеры (протокол ↔ capability).
4. **Данные:** DbGateway + Mongo (read-модель сейчас; event-log/time-series — P0-5/1B).
5. **Контракт (сквозной):** `Domovoy.Contracts` связывает все уровни единой схемой.

## Взаимодействие

- **Сервис ↔ сервис:** только через шину (RabbitMQ), событиями из `Domovoy.Contracts`.
- **Клиент ↔ система:** REST через ApiGateway (команды) + SignalR (live-состояние).
- **Устройство ↔ система:** MQTT (Zigbee2MQTT и Domovoy.Native) через единственный клиент Connectivity.

## Целевая эволюция (см. roadmap)

Текущая топология — **правильный event-driven фундамент**. Дальше наращивается, не переписывается:
`AutomationService` (1A) с детерминированным безопасным полом, Mongo time-series + event-log (P0-5/1B),
**Integration SDK** — обобщение `IProtocolAdapter` во внепроцессные плагины над шиной (1C,
resource-aware), объяснимость+реплей (1F), затем ML-предложения (Фаза 2). Принцип — изоляция отказов:
плагин/сервис падает или обновляется, не роняя остальные.
