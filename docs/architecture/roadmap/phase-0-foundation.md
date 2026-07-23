[← Дорожная карта](../roadmap.md)

# Фаза 0 — Фундамент (фундамент заложен)

**Цель фазы:** привести ядро в состояние, пригодное для роста: единый контракт, обобщённая модель устройства, чистый data-path, базовая безопасность. Без этого автоматизации/ML/плагины «зацементируют» текущий технический долг.

**Выход фазы:** новое устройство любого домена описывается через capability-модель; все события ходят в едином конверте; история событий копится; есть базовая аутентификация.

> **Статус фазы (на 2026-06-01): закрыта.** P0-1 ✅ · P0-2 ✅ · P0-3 ✅ · P0-4 ✅ (интеграционный тест
> адаптер→шина→Mongo против реальных RabbitMQ/Mongo, Фаза 1.5) · P0-5 ✅ · P0-6 ✅. Все .NET-проекты + WebUI
> (`tsc`/lint/тесты) зелёные. Фаза 1 функционально закрыта; план Фазы 2 согласован (см. ниже).
> **Фаза 1.5 идёт:** интеграционный data-path тест + сериализация Mongo проверены вживую (Testcontainers);
> остаётся offline-smoke в CI + полный живой 3-оконный прогон (адаптеры/Zigbee/правила/блоки) — см. `docs/runbook.md`.

> **Ход исполнения (capability-миграция, на 2026-05-27).** Аддитивно, инкрементально, сборка зелёная:
> 1. ✅ `Domovoy.Contracts` — конверт `Envelope<T>` (CloudEvents), **открытая capability-модель**, `BusTopology` (единое именование), детерминированные id (`DeviceIdFactory`).
> 2. ✅ `Zigbee2MqttAdapter` → capability-контракт (`exposes`→capabilities, codec decode/encode) — публикует `DeviceDiscoveredV1`/`DeviceStateReportV1`.
> 3. ✅ `CapabilityDeviceManager` (UnifiedDeviceService) потребляет контракт и переизлучает нормализованное состояние в SignalR.
> 4. ✅ Нативный трек: протокол **Domovoy.Native v1** + `DomovoyNativeAdapter` (near-identity) + эмулятор с веб-UI (:5080) + прошивка `DomovoyClient` v2 (**мультиустройственная**: хаб фронтит N устройств на одну плату).
> 5. ✅ **Шаг 4 (backend):** DbGateway персистит read-модель `capability_devices` (EventInterceptor ← контракт) + GET-эндпоинты + Ocelot-маршрут; ApiGateway публикует `DeviceCommandV1` (`POST /api/device-control/{id}/set`).
> 6. ✅ **Шаг 4 (WebUI):** capability-aware страница `/devices` — список из `GET /api/capability-devices`, контролы по `kind` каждой capability (Switch/Slider/Chip), команды через `POST /api/device-control/{id}/set`, live-состояние по SignalR (`DeviceStateUpdated`). Типизация (`tsc`) зелёная.
> 7. ✅ **Шаг 5 — legacy снят.** Удалено: старый `UnifiedDeviceManager` + `IDeviceTypeHandler` + Generic/Light/Sensor хендлеры + `DeviceIdentityResolver`; legacy события в `IProtocolAdapter` и dual-publish в Z2M + `DomovoyNativeAdapter`; старый `POST /api/device-control/command` и `ZigbeeController.SetDeviceState`; `EventInterceptor`/`EventRelayService` старые подписки; старые `Device`/`Light`/`Sensor`/`MqttDevice`/`BaseEntity`/события/enums/команды в Common; `BaseService`/`IDeviceService`/`IMessageBusHandler`/`OrchestrationCommand`/`AdminController`; legacy `Device` / `DeviceRepository` / `DeviceEndpoints` в DbGateway и Ocelot-маршруты `/api/devices|locations|sensors`; legacy WebUI (`Dashboard`/`api/devices`/`store/deviceStore`/`types/device`/`components/devices`/тесты), индекс — `Devices`; `MessageBusConfiguration` урезан до живых констант; `RabbitMQConnection.ConfigureMqttExchanges` убран (ленивая декларация). Все 7 .NET-проектов и `tsc` фронтенда — зелёные.
>
> Не проверено вживую (нет RabbitMQ/Mongo/Zigbee): рантайм-потоки и Mongo-сериализация. Проводной контракт валидируется собирающимся эмулятором. Пошаговый статус — `src/Common/Domovoy.Contracts/README.md`.

### P0-1. Единый версионируемый контракт событий и команд ✅
- ✅ Конверт сообщений в стиле **CloudEvents** ([Envelope](../../../src/Common/Domovoy.Contracts/Messaging/Envelope.cs): `id`, `specversion`, `type`, `source`, `subject`, `time`, `correlationid`, `data`).
- ✅ Пакет-контракт `Domovoy.Contracts` (payload-схемы + `MessageTypes` с версией `.v1`), от которого зависят все сервисы.
- ✅ Единое именование шины кодифицировано в [BusTopology](../../../src/Common/Domovoy.Contracts/Messaging/BusTopology.cs) (dotted `domovoy.<domain>` exchanges + dotted routing keys) — все продьюсеры/консьюмеры используют константы, «магических строк» нет. Отдельный `message_bus_ru.md` не писали — конвенция живёт в коде (источник истины).
- **DoD:** все publish/subscribe в сервисах/шлюзах используют константы из `Domovoy.Contracts`; схемы версионированы (`v1`). ✅

### P0-2. Capability-модель устройства вместо закрытого enum ✅
- ✅ Закрытый тип (`Light/Sensor/Switch/Generic` + `IDeviceTypeHandler`) **удалён** (Шаг 5); вместо него — **открытая** модель [Capability](../../../src/Common/Domovoy.Contracts/Capabilities/Capability.cs) `(id, kind, attrs)` + [WellKnownCapabilities](../../../src/Common/Domovoy.Contracts/Capabilities/WellKnownCapabilities.cs) (`on_off`, `brightness`, `color_temp`, `temperature`, `humidity`, `co2`, `presence`, `lock`, `valve`, `battery`, … — расширяемый, не enum).
- ✅ Устройство = [DeviceDescriptor](../../../src/Common/Domovoy.Contracts/Devices/DeviceDescriptor.cs) (идентичность + список capability) + состояние по каждой capability.
- ✅ Кодек живёт per-adapter (Z2M `exposes`→capabilities decode/encode), а не «handler per device-type» — расширение без перекомпиляции ядра.
- **DoD:** климат-зона, клапан полива и замок выражаются без новых enum-значений; discovery Zigbee2MQTT мапит описание устройства в набор capability. ✅

### P0-3. Зоны/участок как first-class сущность ✅
- ✅ Модель [Zone](../../../src/Gateway/Domovoy.DbGateway/Models/Zone.cs) (граф area через `ParentZoneId`: этаж → комната; участок → грядка/газон/въезд) в коллекции `zones`; старый неиспользуемый `Location.cs` удалён.
- ✅ CRUD зон в DbGateway ([ZoneEndpoints](../../../src/Gateway/Domovoy.DbGateway/Endpoints/ZoneEndpoints.cs): list/get/create/update/delete; при удалении дети переподвешиваются к родителю, устройства зоны разназначаются) + привязка устройства к зоне `PUT /api/capability-devices/{id}/zone` + фильтр `GET /api/capability-devices?zoneId=`.
- ✅ Проксирование в ApiGateway: [ZonesController](../../../src/Gateway/Domovoy.ApiGateway/Controllers/ZonesController.cs) + расширенный `CapabilityDevicesController` (по образцу прокси; Ocelot из решения убран).
- ✅ `EventInterceptor` больше не затирает ручную привязку зоны при ре-анонсе discovery (`ZoneId` через `SetOnInsert`).
- ✅ WebUI: страница `/zones` (CRUD), группировка устройств по **имени** зоны, селектор зоны в детальном drawer.
- **DoD:** устройство имеет зону; можно получить устройства по зоне (`?zoneId=`); UI показывает группировку по зонам. ✅ (рантайм-проверка с Mongo не прогонялась.)

### P0-4. Единый data-path и устранение дубль-моделей ✅
- ✅ Дубль-модели сняты: старые `Common.Models.Devices.*` и `DbGateway.Models.Device/Light/Sensor` удалены (Шаг 5); единый контракт P0-1 + персистентная read-модель `CapabilityDeviceDocument`.
- ✅ State-path доведён: адаптер → `DeviceStateReportV1` на шине → `CapabilityDeviceManager` → SignalR **и** `EventInterceptor` → персист в Mongo (`capability_devices` + event-log P0-5), без рассинхрона.
- ✅ **Интеграционный тест сквозного пути** (Фаза 1.5): [`tests/Domovoy.IntegrationTests`](../../../tests/Domovoy.IntegrationTests/) поднимает **реальные RabbitMQ + Mongo** (Testcontainers) и проверяет discovery+state по шине → `EventInterceptor` → `capability_devices` (read-модель) + `device_events` (дельта) + `sensor_readings` (телеметрия). Закрыт путь **адаптер→шина→Mongo**; SignalR-плечо (тонкий `EventRelay` ApiGateway) — в живом прогоне, не в авто-тесте.
- **DoD:** одно изменение состояния проходит путь адаптер→шина→Mongo в интеграционном тесте против реальной инфры. ✅ (SignalR-плечо — живой прогон.)

### P0-5. Журнал событий (event-log) как реплейабельный feature store — начать копить данные ✅

> ⚠️ **САМОЕ КРИТИЧНОЕ решение «пока не поздно».** Рвы №2 и №3 ([`positioning_ru.md`](../positioning_ru.md)) —
> обучающийся ML, реплей и объяснимость (Эпик 1F) — строятся **поверх этих данных**. Если начать копить
> *разреженный* лог («что стало»), потом будет невозможно ни обучить ML, ни проиграть историю, ни
> объяснить действие — переснять прошлое неоткуда. Поэтому схему записи фиксируем сразу как feature
> store, а не как «лог».

- Формализовать [EventInterceptor](../../../src/Gateway/Domovoy.DbGateway/Services/EventInterceptor.cs) в **append-only доменный журнал** всех событий устройств/действий пользователя в **MongoDB time-series collection** (та же БД — отдельное хранилище не вводим).
- Поток телеметрии датчиков направить в [SensorReading](../../../src/Gateway/Domovoy.DbGateway/Models/SensorReading.cs) (сейчас модель есть, записи нет) — тоже Mongo time-series.
- **Схема записи (фиксируем сразу, реплейабельный feature store):** каждая запись несёт не только «что стало», но и достаточно для обучения/реплея/объяснимости:
  - `timestamp`, `zone`, `deviceId`, `capabilityId`;
  - **`oldValue → newValue`** (дельта состояния, а не только новое значение);
  - **`triggerSource`** — что вызвало изменение (команда пользователя / правило / адаптер устройства / ML);
  - **`ruleId`/`decisionId`** — какое правило или решение породило действие (связка с [AutoHistory](../../../src/Gateway/Domovoy.DbGateway/Models/AutoHistory.cs) и трассировкой);
  - **`mode`/`context`** — режим дома и контекст на момент события (питает ML-фичи).
- **Разграничение с логированием:** доменный event-log — это *данные* (запросы фич для ML, аудит, реплей), пишется явно в Mongo. **Serilog остаётся для операционной диагностики** (сейчас консоль); опционально позже — Serilog Mongo-sink для персиста ops-логов. Не смешивать одно с другим.
- **DoD:** события и показания датчиков сохраняются с временной меткой, зоной, дельтой состояния и источником-триггером в Mongo time-series; можно выгрузить историю за период И восстановить по ней «кто/что изменил состояние» (это топливо ML и основа реплея/объяснимости из Эпика 1F, поэтому делаем рано и полно).

> **✅ Реализовано (P0-5).** Модель [DeviceEventLog](../../../src/Gateway/Domovoy.DbGateway/Models/DeviceEventLog.cs)
> (полная схема: `oldValue→newValue`, `triggerSource` user/rule/device/ml, `ruleId`/`decisionId`, `mode`/`context`,
> `correlationId`) → time-series коллекция `device_events`; числовая телеметрия → [SensorReading](../../../src/Gateway/Domovoy.DbGateway/Models/SensorReading.cs)
> → time-series `sensor_readings` (создаются на старте через [TimeSeriesInitializer](../../../src/Gateway/Domovoy.DbGateway/Services/TimeSeriesInitializer.cs)).
> [EventInterceptor](../../../src/Gateway/Domovoy.DbGateway/Services/EventInterceptor.cs) пишет дельты (только реальные изменения),
> а триггер атрибутируется по best-effort корреляции с недавней командой (окно 15 c; полная корреляция — Эпик 1F).
> Выгрузка: `GET /api/events` и `GET /api/telemetry` (фильтры deviceId/capabilityId/zoneId/kind/from/to/limit) через
> [HistoryEndpoints](../../../src/Gateway/Domovoy.DbGateway/Endpoints/HistoryEndpoints.cs) + прокси `HistoryController`.
> WebUI: секция «History» в детальном drawer устройства. Serilog оставлен для ops-диагностики, не смешан с доменным логом.
> **Не проверено вживую** против реального Mongo (сериализация time-series, реальная атрибуция).

### P0-6. Снятие auth-трения и наведение порядка ✅
- ✅ JWT переведён в **выключаемый режим** (флаг `JwtSettings:Enabled`, по умолчанию `false`): `AddAuthentication`/`AddJwtBearer`, `UseAuthentication` и Swagger-Bearer подключаются только при `true`. Не выпилен — включается обратно одним флагом.
- ✅ Чистка мёртвого: удалён неиспользуемый `Connectivity/Worker.cs`; `StatusController` приведён к реальной топологии (убраны ссылки на удалённые AuthService/DeviceService/MqttService/UserService/AutomationService и `[Authorize(Roles=Admin)]`).
- **DoD:** локальная разработка и сквозные тесты идут без токенов; решение собирается (0 ошибок) без ссылок на удалённые проекты; мёртвый код удалён. ✅

> ⚠️ Авторизация откладывается осознанно. **Локальная** авторизация «поверх» шлюзов и UI (контроль пользователей и внешних процессов) + TLS на MQTT/шине — это **Фаза 2**, непосредственно перед умными замками и контролем доступа. Внешнего IdP нет — всё локально.
