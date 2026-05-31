# Диаграммы архитектуры Domovoy

> Актуально на 2026-05-31 (после Шага 5 capability-миграции). Прошлые диаграммы (6 сервисов,
> PostgreSQL, Grafana/Loki) относились к снятой архитектуре и удалены.
>
> Связанные: [`layered_architecture_ru.md`](layered_architecture_ru.md), [`database_schema_ru.md`](database_schema_ru.md),
> [`roadmap.md`](roadmap.md), [`../runbook.md`](../runbook.md).

## Обзорная диаграмма системы

```
                          ┌──────────────┐
                          │    WebUI     │  индекс = /devices
                          └──────┬───────┘
                  REST (команды) │  ▲ SignalR (live-состояние)
                                 ▼  │
                          ┌──────────────────────────┐
                          │       ApiGateway          │
                          │ DeviceControl / Capability│
                          │ Zigbee / Status / Metrics │
                          │ EventRelayService+DeviceHub│
                          └──────┬─────────────▲──────┘
                                 │             │
                 ┌───────────────▼─────────────┴───────────────┐
                 │            RabbitMQ  (шина + MQTT)            │
                 │ exchanges: domovoy.discovery/commands/        │
                 │            state/events                       │
                 └───┬───────────────┬───────────────────┬──────┘
                     │               │                   │
                     ▼               ▼                   ▼
          ┌────────────────┐ ┌────────────────┐ ┌────────────────────┐
          │ UnifiedDevice  │ │  Connectivity  │ │     DbGateway       │
          │ Service        │ │ (1 MQTT-клиент)│ │ EventInterceptor →  │
          │ CapabilityDev  │ │ Z2MAdapter +   │ │ Mongo               │
          │ Manager        │ │ NativeAdapter  │ │ capability_devices  │
          └────────────────┘ └───────┬────────┘ └────────────────────┘
                                      │ MQTT
                          ┌───────────┴────────────┐
                          ▼                         ▼
                 ┌─────────────────┐      ┌────────────────────┐
                 │ Zigbee2MQTT     │      │ Domovoy.Native v1   │
                 │ устройства      │      │ (DIY / эмулятор)    │
                 └─────────────────┘      └────────────────────┘

  Мониторинг: Prometheus (метрики со всех сервисов).  Хранилище: MongoDB.
  Нет: Ocelot, Redis, PostgreSQL, Grafana, Loki.
```

## Поток discovery + состояния (запись)

```
Устройство ─MQTT─► Connectivity (адаптер декодирует в capability)
                        │
                        │ Envelope<DeviceDiscoveredV1>   → domovoy.discovery / device.discovered
                        │ Envelope<DeviceStateReportV1>  → domovoy.state     / device.state.updated
                        │ Envelope<DeviceOnlineChangedV1>→ domovoy.events    / device.online.changed
                        ▼
                   RabbitMQ ──────────────┬───────────────────────────┐
                                          ▼                           ▼
                              DbGateway.EventInterceptor      UnifiedDeviceService
                              upsert capability_devices       CapabilityDeviceManager
                                  (Mongo)                     переизлучает состояние
                                                                      │
                                          ApiGateway.EventRelayService │
                                          ── SignalR DeviceHub ────────┴──► WebUI (live)
```

## Поток команды

```
WebUI ─► POST /api/device-control/{id}/set   body: { "on_off": true, "brightness": 50 }
            │
            ▼
   ApiGateway.DeviceControlController
            │ Envelope<DeviceCommandV1>(DeviceId, Set) → domovoy.commands / device.command
            ▼
        RabbitMQ
            │
            ▼
   Connectivity: владеющий адаптер кодирует capability-set в протокол устройства
            │ MQTT
            ▼
      Устройство применяет → публикует новое состояние (см. поток выше)
```

## Контракт сообщений (Domovoy.Contracts)

```
Envelope<T>  (CloudEvents: id, type, source, subject, time, dataschema, data)
   type ∈ { domovoy.device.discovered.v1, .state.v1, .command.v1, .online.v1 }

BusTopology
   exchanges (topic):  domovoy.discovery | domovoy.commands | domovoy.state | domovoy.events
   routing keys:       device.discovered | device.command | device.state.updated | device.online.changed

Payloads
   DeviceDiscoveredV1(DeviceDescriptor)              — устройство обнаружено/переанонсировано
   DeviceStateReportV1(DeviceId, State: cap→value)   — нормализованное состояние
   DeviceCommandV1(DeviceId, Set: cap→value)         — capability-адресованная команда
   DeviceOnlineChangedV1(DeviceId, IsOnline)         — доступность
```

## Среда разработки / развёртывание

```
┌──────────────────────── Docker (homelab) ─────────────────────────┐
│  Domovoy-сервисы        RabbitMQ (bus+MQTT)        MongoDB          │
│  (Connectivity,         Zigbee2MQTT                Prometheus       │
│   UnifiedDeviceService,                                            │
│   ApiGateway, DbGateway)                                           │
└────────────────────────────────────────────────────────────────────┘
        ▲ (вне Docker, намеренно)
   Domovoy.DeviceEmulator  — виртуальные Native v1 устройства + веб-UI :5080
```

> Развёртывание — **homelab** (мини-ПК → ПК → стойка), совмещён с другими задачами →
> resource-aware модульность (плагины Фазы 1C включаются по наличию CPU/RAM/GPU). Сценарий запуска
> и 3-оконный тест (server WebUI ↔ логи ↔ эмулятор) — в [`../runbook.md`](../runbook.md).
