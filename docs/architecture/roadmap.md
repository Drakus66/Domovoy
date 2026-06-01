# Дорожная карта Domovoy

> Документ описывает путь от текущего состояния (сервер управления устройствами) к целевому
> продукту — **автоматизированному умному дому с ML, плагинами и локальным голосовым управлением**.
> Детализация намеренно убывает: ближайшие фазы расписаны по задачам с критериями готовности,
> дальние — на уровне направлений и концептов.
>
> Связанные документы: [`memory-bank/currentState.md`](../../memory-bank/currentState.md) (актуальное
> состояние кода после Шага 5, 2026-05-27), [`docs/runbook.md`](../runbook.md) (запуск стека и
> 3-оконный сценарий), [`docs/architecture/positioning_ru.md`](positioning_ru.md) (позиционирование,
> три не-клонируемых рва, конкурентный анализ — линза для решений «делать/не делать»),
> [`docs/architecture/layered_architecture_ru.md`](layered_architecture_ru.md)
> (устарело — историческая 6-сервисная архитектура).

## Легенда статусов

- ✅ сделано · 🚧 в работе · ⬜ запланировано · 🔬 концепт/исследование
- **DoD** — Definition of Done (критерий готовности).

## Опорные принципы (действуют на всех фазах)

1. **Слоистое управление.** Детерминированный безопасный пол (правила/расписания) → пользовательские уставки → ML-оптимизация сверху. Критичные домены (отопление, доступ) никогда не зависят только от ML/облака/UI.
2. **Offline-first как проверяемый инвариант (не просто принцип).** Local-first сегодня декларируют все (HA, Homey) — поэтому наше отличие не в философии, а в гарантии: **облако физически не может оказаться в критическом пути**, и это проверяется тестом (smoke «всё ядро работает с отключённой внешней сетью», см. раздел «Тестирование»). Реакции реального времени (свет по движению, голос, замки), автоматизации, ML и хранилище — локальные. Облачные интеграции (пылесос, газонокосилка) — **опциональные плагины**, которые при отсутствии интернета просто не работают, не затрагивая базовый функционал.
3. **Resource-aware модульность.** Развёртывание — homelab (от мини-ПК до стойки) с другими задачами. Плагины декларируют потребности (CPU/RAM/GPU); супервизор включает тяжёлые компоненты только при наличии мощностей, иначе — базовый функционал. Масштабирование «вверх» добавлением плагинов, а не переписыванием ядра.
4. **Contract-first.** Плагины, ML и сервисы привязываются к версионируемой схеме событий/команд, а не к внутренним классам.
5. **Fail-safe и модульность сервисов.** Остановка/обновление одного сервиса/плагина не валит остальные; критичное состояние восстанавливается из БД, а не живёт только в памяти.
6. **Сначала данные, потом ML.** Сбор телеметрии и журнала событий запускается задолго до моделей.

---

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
- ✅ Конверт сообщений в стиле **CloudEvents** ([Envelope](../../src/Common/Domovoy.Contracts/Messaging/Envelope.cs): `id`, `specversion`, `type`, `source`, `subject`, `time`, `correlationid`, `data`).
- ✅ Пакет-контракт `Domovoy.Contracts` (payload-схемы + `MessageTypes` с версией `.v1`), от которого зависят все сервисы.
- ✅ Единое именование шины кодифицировано в [BusTopology](../../src/Common/Domovoy.Contracts/Messaging/BusTopology.cs) (dotted `domovoy.<domain>` exchanges + dotted routing keys) — все продьюсеры/консьюмеры используют константы, «магических строк» нет. Отдельный `message_bus_ru.md` не писали — конвенция живёт в коде (источник истины).
- **DoD:** все publish/subscribe в сервисах/шлюзах используют константы из `Domovoy.Contracts`; схемы версионированы (`v1`). ✅

### P0-2. Capability-модель устройства вместо закрытого enum ✅
- ✅ Закрытый тип (`Light/Sensor/Switch/Generic` + `IDeviceTypeHandler`) **удалён** (Шаг 5); вместо него — **открытая** модель [Capability](../../src/Common/Domovoy.Contracts/Capabilities/Capability.cs) `(id, kind, attrs)` + [WellKnownCapabilities](../../src/Common/Domovoy.Contracts/Capabilities/WellKnownCapabilities.cs) (`on_off`, `brightness`, `color_temp`, `temperature`, `humidity`, `co2`, `presence`, `lock`, `valve`, `battery`, … — расширяемый, не enum).
- ✅ Устройство = [DeviceDescriptor](../../src/Common/Domovoy.Contracts/Devices/DeviceDescriptor.cs) (идентичность + список capability) + состояние по каждой capability.
- ✅ Кодек живёт per-adapter (Z2M `exposes`→capabilities decode/encode), а не «handler per device-type» — расширение без перекомпиляции ядра.
- **DoD:** климат-зона, клапан полива и замок выражаются без новых enum-значений; discovery Zigbee2MQTT мапит описание устройства в набор capability. ✅

### P0-3. Зоны/участок как first-class сущность ✅
- ✅ Модель [Zone](../../src/Gateway/Domovoy.DbGateway/Models/Zone.cs) (граф area через `ParentZoneId`: этаж → комната; участок → грядка/газон/въезд) в коллекции `zones`; старый неиспользуемый `Location.cs` удалён.
- ✅ CRUD зон в DbGateway ([ZoneEndpoints](../../src/Gateway/Domovoy.DbGateway/Endpoints/ZoneEndpoints.cs): list/get/create/update/delete; при удалении дети переподвешиваются к родителю, устройства зоны разназначаются) + привязка устройства к зоне `PUT /api/capability-devices/{id}/zone` + фильтр `GET /api/capability-devices?zoneId=`.
- ✅ Проксирование в ApiGateway: [ZonesController](../../src/Gateway/Domovoy.ApiGateway/Controllers/ZonesController.cs) + расширенный `CapabilityDevicesController` (по образцу прокси; Ocelot из решения убран).
- ✅ `EventInterceptor` больше не затирает ручную привязку зоны при ре-анонсе discovery (`ZoneId` через `SetOnInsert`).
- ✅ WebUI: страница `/zones` (CRUD), группировка устройств по **имени** зоны, селектор зоны в детальном drawer.
- **DoD:** устройство имеет зону; можно получить устройства по зоне (`?zoneId=`); UI показывает группировку по зонам. ✅ (рантайм-проверка с Mongo не прогонялась.)

### P0-4. Единый data-path и устранение дубль-моделей ✅
- ✅ Дубль-модели сняты: старые `Common.Models.Devices.*` и `DbGateway.Models.Device/Light/Sensor` удалены (Шаг 5); единый контракт P0-1 + персистентная read-модель `CapabilityDeviceDocument`.
- ✅ State-path доведён: адаптер → `DeviceStateReportV1` на шине → `CapabilityDeviceManager` → SignalR **и** `EventInterceptor` → персист в Mongo (`capability_devices` + event-log P0-5), без рассинхрона.
- ✅ **Интеграционный тест сквозного пути** (Фаза 1.5): [`tests/Domovoy.IntegrationTests`](../../tests/Domovoy.IntegrationTests/) поднимает **реальные RabbitMQ + Mongo** (Testcontainers) и проверяет discovery+state по шине → `EventInterceptor` → `capability_devices` (read-модель) + `device_events` (дельта) + `sensor_readings` (телеметрия). Закрыт путь **адаптер→шина→Mongo**; SignalR-плечо (тонкий `EventRelay` ApiGateway) — в живом прогоне, не в авто-тесте.
- **DoD:** одно изменение состояния проходит путь адаптер→шина→Mongo в интеграционном тесте против реальной инфры. ✅ (SignalR-плечо — живой прогон.)

### P0-5. Журнал событий (event-log) как реплейабельный feature store — начать копить данные ✅

> ⚠️ **САМОЕ КРИТИЧНОЕ решение «пока не поздно».** Рвы №2 и №3 ([`positioning_ru.md`](positioning_ru.md)) —
> обучающийся ML, реплей и объяснимость (Эпик 1F) — строятся **поверх этих данных**. Если начать копить
> *разреженный* лог («что стало»), потом будет невозможно ни обучить ML, ни проиграть историю, ни
> объяснить действие — переснять прошлое неоткуда. Поэтому схему записи фиксируем сразу как feature
> store, а не как «лог».

- Формализовать [EventInterceptor](../../src/Gateway/Domovoy.DbGateway/Services/EventInterceptor.cs) в **append-only доменный журнал** всех событий устройств/действий пользователя в **MongoDB time-series collection** (та же БД — отдельное хранилище не вводим).
- Поток телеметрии датчиков направить в [SensorReading](../../src/Gateway/Domovoy.DbGateway/Models/SensorReading.cs) (сейчас модель есть, записи нет) — тоже Mongo time-series.
- **Схема записи (фиксируем сразу, реплейабельный feature store):** каждая запись несёт не только «что стало», но и достаточно для обучения/реплея/объяснимости:
  - `timestamp`, `zone`, `deviceId`, `capabilityId`;
  - **`oldValue → newValue`** (дельта состояния, а не только новое значение);
  - **`triggerSource`** — что вызвало изменение (команда пользователя / правило / адаптер устройства / ML);
  - **`ruleId`/`decisionId`** — какое правило или решение породило действие (связка с [AutoHistory](../../src/Gateway/Domovoy.DbGateway/Models/AutoHistory.cs) и трассировкой);
  - **`mode`/`context`** — режим дома и контекст на момент события (питает ML-фичи).
- **Разграничение с логированием:** доменный event-log — это *данные* (запросы фич для ML, аудит, реплей), пишется явно в Mongo. **Serilog остаётся для операционной диагностики** (сейчас консоль); опционально позже — Serilog Mongo-sink для персиста ops-логов. Не смешивать одно с другим.
- **DoD:** события и показания датчиков сохраняются с временной меткой, зоной, дельтой состояния и источником-триггером в Mongo time-series; можно выгрузить историю за период И восстановить по ней «кто/что изменил состояние» (это топливо ML и основа реплея/объяснимости из Эпика 1F, поэтому делаем рано и полно).

> **✅ Реализовано (P0-5).** Модель [DeviceEventLog](../../src/Gateway/Domovoy.DbGateway/Models/DeviceEventLog.cs)
> (полная схема: `oldValue→newValue`, `triggerSource` user/rule/device/ml, `ruleId`/`decisionId`, `mode`/`context`,
> `correlationId`) → time-series коллекция `device_events`; числовая телеметрия → [SensorReading](../../src/Gateway/Domovoy.DbGateway/Models/SensorReading.cs)
> → time-series `sensor_readings` (создаются на старте через [TimeSeriesInitializer](../../src/Gateway/Domovoy.DbGateway/Services/TimeSeriesInitializer.cs)).
> [EventInterceptor](../../src/Gateway/Domovoy.DbGateway/Services/EventInterceptor.cs) пишет дельты (только реальные изменения),
> а триггер атрибутируется по best-effort корреляции с недавней командой (окно 15 c; полная корреляция — Эпик 1F).
> Выгрузка: `GET /api/events` и `GET /api/telemetry` (фильтры deviceId/capabilityId/zoneId/kind/from/to/limit) через
> [HistoryEndpoints](../../src/Gateway/Domovoy.DbGateway/Endpoints/HistoryEndpoints.cs) + прокси `HistoryController`.
> WebUI: секция «History» в детальном drawer устройства. Serilog оставлен для ops-диагностики, не смешан с доменным логом.
> **Не проверено вживую** против реального Mongo (сериализация time-series, реальная атрибуция).

### P0-6. Снятие auth-трения и наведение порядка ✅
- ✅ JWT переведён в **выключаемый режим** (флаг `JwtSettings:Enabled`, по умолчанию `false`): `AddAuthentication`/`AddJwtBearer`, `UseAuthentication` и Swagger-Bearer подключаются только при `true`. Не выпилен — включается обратно одним флагом.
- ✅ Чистка мёртвого: удалён неиспользуемый `Connectivity/Worker.cs`; `StatusController` приведён к реальной топологии (убраны ссылки на удалённые AuthService/DeviceService/MqttService/UserService/AutomationService и `[Authorize(Roles=Admin)]`).
- **DoD:** локальная разработка и сквозные тесты идут без токенов; решение собирается (0 ошибок) без ссылок на удалённые проекты; мёртвый код удалён. ✅

> ⚠️ Авторизация откладывается осознанно. **Локальная** авторизация «поверх» шлюзов и UI (контроль пользователей и внешних процессов) + TLS на MQTT/шине — это **Фаза 2**, непосредственно перед умными замками и контролем доступа. Внешнего IdP нет — всё локально.

---

# Фаза 1 — Автоматизация и платформа данных (СЕЙЧАС)

**Цель фазы:** продукт начинает работать «по сценариям» сам; появляется фундамент данных для ML и SDK для расширений.

> **Статус фазы (на 2026-06-01): функционально закрыта.** 1A ✅ · 1B ✅ · 1C ✅ (супервизор-фундамент) ·
> 1D ✅ · 1E ✅ (визуальный flow-редактор) · 1F ✅ · 1G ✅ · 1H ✅ (E1-фундамент). Все эпики Фазы 1 имеют
> рабочую реализацию; остаются «дальше»-хвосты внутри 1C/1H (ALC, E2-композиты и т.п.).
> **Рантайм против реального RabbitMQ/Mongo не прогонялся.**

### Эпик 1A. AutomationService — детерминированный движок ✅
Отдельный сервис, подписан на шину, держит состояние правил.

> **✅ Реализовано (Epic 1A).** Новый сервис [`Domovoy.AutomationService`](../../src/Services/Domovoy.AutomationService/):
> модель правила в контракте [`AutomationRule`](../../src/Common/Domovoy.Contracts/Automations/AutomationRule.cs)
> (`trigger → condition → action`, статусы, `IsProtected`). Триггеры: device-state (`AutomationEngine`
> подписан на `DeviceStateReportV1`), время (cron, `CronSchedule` — мини-парсер без зависимостей) и
> солнце (`SunCalculator` — sunrise/sunset офлайн); планировщик `AutomationScheduler` тикает раз в минуту.
> Условия: device/zone-state, time-of-day, sun (темно/светло), mode (заглушка под 1G). Действия:
> команда (`DeviceCommandV1` с `source=automation:{ruleId}` → P0-5 атрибутирует `triggerSource=rule`),
> задержка (даёт «свет на 5 мин» = on → delay → off), уведомление. `ActionExecutor` исполняет на фоне,
> публикует `AutomationTriggeredV1` → DbGateway пишет `AutoHistory`. Правила: CRUD `/api/automations` в
> DbGateway + прокси `AutomationsController`; `RuleStore` грузит их по HTTP + **безопасный пол из
> локального `safety-rules.json`** (protected, не отключаются из UI, работают даже без БД/UI). WebUI —
> страница `/automations` (список, toggle, удаление, история, мини-конструктор правила). Все 8 .NET-
> проектов + WebUI `tsc`/lint/26 тестов зелёные. **Не проверено вживую** против RabbitMQ/Mongo.
- **Модель правила** `trigger → condition → action` в контракте [AutomationRule](../../src/Common/Domovoy.Contracts/Automations/AutomationRule.cs):
  - триггеры: событие устройства/зоны, изменение состояния, время, **солнечные события** (восход/закат) — для наружного света;
  - условия: состояние устройств/зон, время суток, присутствие, режим дома;
  - действия: команды устройствам, установка уставок, уведомления, задержки/таймеры.
- **Планировщик:** cron-расписания (полив), таймеры, sun-based триггеры.
- **История срабатываний** в [AutoHistory](../../src/Gateway/Domovoy.DbGateway/Models/AutoHistory.cs).
- **Безопасный пол:** защитные правила, которые нельзя отключить из UI (антизамерзание, CO2 > порога → вентиляция, дым → разблокировать замки).
- **Очередь предложений с апрувом:** статусы правила `Proposed → Approved → Active → Disabled` (заранее — под ML из Фазы 2).
- **DoD:** правило «движение в коридоре после заката → включить свет на 5 мин» создаётся, сохраняется, срабатывает и пишется в историю; защитное правило работает даже при недоступном UI. ✅ (выражается через device-state триггер + sun-условие `dark` + действия command/delay/command; safety floor — из локального файла. Рантайм с RabbitMQ/Mongo не прогонялся.)

### Эпик 1B. Платформа данных для телеметрии ✅
- Использовать **MongoDB time-series collections** (решение принято: единая БД, отдельный TSDB не вводим) рядом с текущим состоянием в Mongo и журналом из P0-5.
- Ретеншн-политики, агрегация (минутные/часовые свёртки), экспорт за период.
- **DoD:** показания климат-датчиков пишутся в Mongo time-series; график «температура зоны за сутки» строится из API.

> **✅ Реализовано (Epic 1B).** Поверх готового `sensor_readings` (P0-5, запись телеметрии уже шла).
> **Ретеншн:** [`TelemetryOptions.RawRetentionDays`](../../src/Gateway/Domovoy.DbGateway/Config/TelemetryOptions.cs)
> (0 = хранить вечно) → TTL на `sensor_readings` через
> [`TimeSeriesInitializer`](../../src/Gateway/Domovoy.DbGateway/Services/TimeSeriesInitializer.cs)
> (`ExpireAfter` при создании + `collMod` на старте, чтобы смена политики применялась). **Доменный
> event-log `device_events` сознательно НЕ истекает** — это реплейабельный feature store (P0-5/1F).
> **Агрегация (свёртки):** `GET /api/telemetry/aggregate?bucket=minute|hour|day&agg=avg|min|max` —
> on-the-fly свёртки через Mongo `$dateTrunc`+`$group` (avg/min/max/count на бакет; фильтры
> deviceId/capabilityId/**zoneId**/from/to), в [`HistoryEndpoints`](../../src/Gateway/Domovoy.DbGateway/Endpoints/HistoryEndpoints.cs).
> Отдельный rollup-store не вводим (homelab): свёртки считаются из сырых семплов в пределах окна ретеншна.
> **Экспорт за период:** `GET /api/telemetry?...&format=csv`. Прокси в ApiGateway `HistoryController`.
> **WebUI:** компонент [`TelemetryChart`](../../src/UI/WebUI/src/components/charts/TelemetryChart.tsx)
> (recharts area-chart, 24ч/часовые бакеты, scope по device **или** zone) — встроен в детальный drawer
> устройства секцией «Trends · last 24h» для числовых сенсоров. График «температура зоны за сутки»
> строится тем же эндпоинтом/компонентом по `zoneId`. 8 .NET-проектов + `tsc`/lint/26 тестов зелёные.
> **Не проверено вживую** против Mongo (агрегация `$dateTrunc`, TTL `collMod`).

### Эпик 1C. Integration SDK — внепроцессные плагины поверх шины ✅ (супервизор-фундамент)
- Обобщить [IProtocolAdapter](../../src/Services/Domovoy.Connectivity/Adapters/IProtocolAdapter.cs) из «адаптера протокола» в **«интеграцию»**.
- **Манифест плагина:** id, версия, предоставляемые capability, подписки на события, принимаемые команды, требуемые права, **требуемые ресурсы (CPU/RAM/GPU, интернет)**.
- **Контрактный пакет** = `Domovoy.Contracts` (P0-1): для .NET — NuGet, для Python — pip-пакет со схемами (под будущий ML/голос).
- **Реестр + супервизор:** обнаружение, запуск/остановка/health, версии, изоляция прав. Плагин — отдельный процесс; падение/обновление не влияет на ядро.
- **Resource-aware включение:** супервизор сверяет требования манифеста с мощностями хоста (homelab) и доступностью интернета — тяжёлые/облачные плагины включаются только при наличии ресурсов, иначе система работает на базовом функционале.
- Гибрид: лёгкие доверенные first-party расширения допустимо грузить в процесс через `AssemblyLoadContext` (collectible, выгрузка без рестарта).
- **DoD:** новый адаптер устройства подключается как плагин по манифесту, без перекомпиляции ядра; остановка плагина не роняет остальные сервисы; плагин с невыполнимыми требованиями ресурсов автоматически не активируется.

> **✅ Реализовано (Epic 1C — супервизор-фундамент).** Новый сервис
> [`Domovoy.PluginSupervisor`](../../src/Services/Domovoy.PluginSupervisor/) (WebApplication, Kestrel :8080,
> метрики :9090). **Манифест** [`PluginManifest`](../../src/Common/Domovoy.Contracts/Plugins/PluginManifest.cs)
> (id/version/capabilities/subscriptions/commands/permissions + `ResourceRequirements` cpu/ram/gpu/internet +
> autostart/enabled), обнаруживается как `plugin.json` в подпапках plugins-root. **Resource-aware гейтинг:**
> [`HostResources`](../../src/Services/Domovoy.PluginSupervisor/Resources/HostResources.cs) (cpu/ram из рантайма,
> gpu/internet из конфига) × требования манифеста → плагин с невыполнимыми ресурсами помечается `Blocked` и не
> запускается (DoD #3). **Супервизор** [`PluginSupervisor`](../../src/Services/Domovoy.PluginSupervisor/Plugins/PluginSupervisor.cs):
> запускает удовлетворимые плагины как **отдельные процессы** (изоляция — падение плагина не роняет ядро, DoD #2),
> мониторит выходы, рестартит с capped-backoff; start/stop через API. Плагин сам подключается к шине и говорит
> capability-контракт → новый адаптер по манифесту без перекомпиляции ядра (DoD #1). HTTP: `GET /api/plugins[/{id}]`,
> `POST /api/plugins/{id}/start|stop`; прокси `PluginsController` в ApiGateway. WebUI `/plugins` (статусы, ресурсы
> хоста, start/stop). Пример манифеста `plugins/example-anpr-camera/` (требует GPU → Blocked на хосте без GPU).
> 9 .NET-проектов + `tsc`/lint/26 тестов зелёные. **Не проверено вживую.**
> **Дальше:** in-process ALC для доверенных first-party (выгрузка без рестарта), контейнер-на-плагин (Docker API),
> live-probe интернета, enforcement прав (с локальной авторизацией Фазы 2), Python-pip контрактный пакет (под ML/голос).

### Эпик 1D. Capability-адаптеры под целевые домены ✅
- Климат: датчики температуры/влажности/CO2 → управление вентиляцией и нагревателями/охладителями (по зоне).
- Отопление: регулировка тёплого пола (уставка по зоне).
- Участок: контроллеры полива (клапаны, расписание), наружное освещение.
- Switch/реле общего назначения.
- **Движок исполнения этих доменов — control blocks из [Эпика 1H](#эпик-1h-control-blocks--контурноеблочное-управление):** климат/тёплый пол/полив — это контуры и секвенсоры, которые правилами (1A) не выражаются. 1A дёргает события и уставки, **1H держит контуры**.
- **DoD:** каждая capability имеет адаптер и сквозной путь команда→исполнение→подтверждение состояния; домены управляются правилами из 1A и контурами из 1H.

> **✅ Реализовано (Epic 1D).** Capability-адаптеры (Z2M + Native) и путь команда→исполнение→state
> существуют с Фазы 0; 1D добавил **доменную логику управления как control blocks (1H)** и **актуацию**.
> **Actuation:** [`ControlBlock.Outputs`](../../src/Common/Domovoy.Contracts/Blocks/ControlBlock.cs) —
> выход блока привязывается к реальному device+capability; [`BlockRuntime`](../../src/Services/Domovoy.AutomationService/Blocks/BlockRuntime.cs)
> публикует `DeviceCommandV1` (`source=block:{id}`) при изменении значения (dedup) → P0-5 атрибутирует,
> адаптер исполняет, state возвращается на шину (замыкает «команда→исполнение→подтверждение»). **Доменные
> контуры** ([`BuiltInBlocks.cs`](../../src/Services/Domovoy.AutomationService/Blocks/BuiltInBlocks.cs)):
> термостат (отопление, 1H) + **CO₂-вентиляция** (климат: co2→демпинг вентиляции с гистерезисом) +
> **секвенсор полива** (участок: state-machine — клапан на `runMinutes` каждые `intervalHours`, вход
> `inhibit` для дождь/ET-коррекции). Switch/реле = capability `on_off` (тривиально). WebUI `/blocks`:
> привязка выходов к актуаторам в форме создания + «Drives …» в карточке. 8 .NET-проектов + `tsc`/lint/26
> тестов зелёные. **Не проверено вживую** против RabbitMQ/Mongo.
> **Дальше:** охлаждение/heat-cool режим, многозонный полив как E2-композит, наружное освещение по sun/1A.

### Эпик 1E. UI: сценарии и зоны ✅
- Редактор автоматизаций (trigger/condition/action), просмотр истории срабатываний, очередь предложений на апрув.
- Представление по зонам; уставки (режимы) пользователя.
- Снизить зависимость Dashboard от поллинга в пользу SignalR.
- **Ориентир UX — Homey Advanced Flow**, а НЕ дашборды Lovelace HA: цель — простой и наглядный редактор правил при тезисе «меньше UI», а не богатство дашбордов (см. [`positioning_ru.md`](positioning_ru.md), «чем мы НЕ являемся»).
- **DoD:** пользователь создаёт/редактирует правило и задаёт уставки зон из UI; видит реалтайм-состояние.

> **✅ Реализовано (Epic 1E).** **Визуальный flow-редактор** на **React Flow** (ориентир Homey Advanced Flow)
> — страница [`Flow.tsx`](../../src/UI/WebUI/src/pages/Flow.tsx): правило рисуется графом **When (триггер) →
> And if (условия) → Then (действия)** карточками со стрелками; клик по карточке открывает редактор узла в
> боковой панели (типы/устройство/capability/оператор/значение, cron, sun, mode, delay, notify). Создание/
> загрузка/редактирование/добавление-удаление узлов и **сохранение** в существующий `/api/automations`
> (создаёт/обновляет `AutomationRule`, включая статус Active/Shadow из 1F). **Реалтайм:** в карточках/полях
> показываются live-значения устройств (`now: …`), подтягиваются периодически из `/api/capability-devices`
> (SignalR-realtime на `/devices` уже был). История срабатываний — на странице `/automations` (1A/1F).
> **Уставки зон** реализованы через термостат-блоки 1D (`temperature_setpoint` командой) + режимы 1G; зоны —
> группировка на `/devices`/`/zones` (P0-3). 9 .NET-проектов + WebUI `tsc`/lint/26 тестов + `vite build`
> зелёные. **Не проверено вживую.**
> **Дальше:** визуальный редактор графа control-blocks (тот же React Flow над `control_blocks`), очередь
> предложений на апрув (под ML Фазы 2), drag-раскладка с сохранением позиций, мульти-триггер.

### Эпик 1F. Объяснимость и реплей/симуляция автоматизаций ✅

> Это **ров №2** ([`positioning_ru.md`](positioning_ru.md)) и главное отличие от всех конкурентов.
> Почти бесплатно вытекает из event-log (P0-5) — но только если его схема собрана полно. Это мост
> доверия к ML из Фазы 2: предложение модели валидируется против реальной истории до активации.

- **Объяснимость действий:** каждое действие системы трассируется к правилу/предложению + событию-триггеру («почему включился свет»). Опирается на `triggerSource`/`ruleId`/`decisionId` из P0-5 и на [AutoHistory](../../src/Gateway/Domovoy.DbGateway/Models/AutoHistory.cs).
- **Реплей/симуляция:** проиграть исторический поток событий через движок правил (1A) → «как сработало бы это правило на прошлой неделе» БЕЗ исполнения реальных команд (dry-run против истории).
- **Стадийный выкат правила:** `Proposed → Shadow (логирует, что сделал бы) → Bounded-Active (в безопасных границах) → Full` — поверх статусов правил из 1A.
- **DoD:** для любого совершённого действия UI показывает причину (правило + триггер); новое правило можно прогнать на истории за период и увидеть, когда оно сработало бы, до его активации.

> **✅ Реализовано (Epic 1F).** **Реплей/симуляция:** [`ReplayService`](../../src/Services/Domovoy.AutomationService/Services/ReplayService.cs)
> прогоняет правило-кандидат по историческим device-дельтам из event-log (P0-5): реконструирует состояние
> устройств по ходу и оценивает device-state триггеры + условия в каждой точке тем же `RuleEvaluator`, что и
> живой движок (симуляция == прод-семантика), **без публикации команд**. Условия считаются по локальному
> времени события и `mode`, штампованному на записи (1G). AutomationService переведён на WebApplication
> (Kestrel :8080, метрики остаются на :9090) и отдаёт `POST /api/replay` (правило + окно дней → когда бы
> сработало); прокси `ReplayController` в ApiGateway (+ httpclient `automation-service`). Время/sun-триггеры
> в реплее помечаются как не симулируемые. **Объяснимость:** в детальном drawer устройства изменения с
> `triggerSource=rule` показывают чип «via <имя правила>» (атрибуция через `correlationId`→правило). Полный
> trace срабатываний — уже в `auto_history` (страница `/automations` → History, `triggerSummary`).
> **Стадийный выкат:** добавлен статус [`RuleStatus.Shadow`](../../src/Common/Domovoy.Contracts/Automations/AutomationRule.cs) —
> Shadow-правила оцениваются и пишут в историю, что бы они сделали, **но команд не публикуют** (`ActionExecutor`);
> WebUI: статус-селектор Active/Shadow/Disabled на правиле + создание в Shadow + кнопка «Simulate». 8 .NET-
> проектов + `tsc`/lint/26 тестов зелёные. **Не проверено вживую** против RabbitMQ/Mongo.
> Дальше (Фаза 2): `Bounded-Active` и валидация ML-предложений реплеем перед активацией.

### Эпик 1G. Режимы дома и присутствие как контекст ✅

> Поднято из Фазы 2: безопасный пол (1A) и будущий ML опираются на контекст, а сделать его дёшево —
> инверсия зависимостей, если оставлять в Фазе 2.

- Режимы **Дома / Нет никого / Ночь / Отпуск** как явный контекст для триггеров, условий и климата.
- Источник присутствия (устройства/сенсоры) → переключение режима; ручное переключение из UI.
- Режим/контекст пишется в event-log (P0-5) как фича.
- **DoD:** правило может зависеть от режима дома; смена режима меняет поведение климата/света; режим виден в UI и в журнале.

> **✅ Реализовано (Epic 1G).** **Открытая** модель режимов в контракте
> ([`WellKnownModes`](../../src/Common/Domovoy.Contracts/Home/HomeMode.cs): `Home/Away/Night/Vacation`,
> строки-расширяемо, а не enum) + событие [`HomeModeChangedV1`](../../src/Common/Domovoy.Contracts/Messaging/Payloads.cs).
> **DbGateway — авторитет персиста:** single-doc коллекция `home_state`
> ([`HomeState`](../../src/Gateway/Domovoy.DbGateway/Models/HomeState.cs)) + эндпоинты
> [`ModeEndpoints`](../../src/Gateway/Domovoy.DbGateway/Endpoints/ModeEndpoints.cs) (`GET /api/mode`,
> `GET /api/mode/options`, `PUT /api/mode` — апсертит и публикует `HomeModeChangedV1` на шину при реальной
> смене). Прокси [`ModeController`](../../src/Gateway/Domovoy.ApiGateway/Controllers/ModeController.cs).
> **event-log как фича (P0-5):** [`EventInterceptor`](../../src/Gateway/Domovoy.DbGateway/Services/EventInterceptor.cs)
> подписан на `HomeModeChangedV1`, держит текущий режим (засев из `home_state` на старте) и **штампует `Mode`
> на каждую запись `device_events`** + пишет отдельную запись `mode_change` (capability `home_mode`,
> `old→new`). **AutomationService:** `HomeModeState` (singleton) + `HomeModeMonitor` (слушает шину) →
> `RuleRunner` теперь передаёт реальный режим в условия `Mode` (раньше был `null`); засев/ремонт режима в
> `RefreshLoop` из DbGateway. **Присутствие → авто-режим:** `PresenceMonitor` слушает capability
> `presence`/`occupancy`, переводит Away→Home при детекте и Home→Away после `AwayDelaySeconds` тишины
> (настройки в `AutomationOptions`); **никогда не перебивает ручные Night/Vacation**. **WebUI:** страница
> `/modes` (карточки режимов, текущий режим + источник, недавние смены из event-log). 8 .NET-проектов +
> `tsc`/lint/26 тестов зелёные. **Не проверено вживую** против RabbitMQ/Mongo.

### Эпик 1H. Control blocks — контурное/блочное управление ✅ (E1-фундамент)

> **Недостающая середина** между правилами (1A) и сервисами. Дизайн зафиксирован (2026-06-01); реализация —
> по мере прохождения роадмэпа. Это **движок исполнения для 1D** (климат/тёплый пол/полив) и продолжение
> слоистой модели (принцип 1): 1A дёргает события и уставки, **1H держит контуры**, безопасный пол вето сверху,
> ML (Фаза 2) предлагает уставки/параметры снизу — блок исполняет их детерминированно.

> **✅ Реализовано (Epic 1H — E1-фундамент).** **Block-SDK** ([`BlockSdk.cs`](../../src/Services/Domovoy.AutomationService/Blocks/BlockSdk.cs)):
> `IBlock` (`Tick`), `IBlockType` (TypeId + схема портов/параметров/выходов + фабрика), `IBlockContext`
> (`Read`/`ReadNumber`/`Param`/`Commanded`/`Emit`/`GetState`/`SetState`/`Now`/`Log`). **Каталог** built-in
> типов ([`BlockCatalog`](../../src/Services/Domovoy.AutomationService/Blocks/BlockCatalog.cs)) + **рантайм**
> ([`BlockRuntime`](../../src/Services/Domovoy.AutomationService/Blocks/BlockRuntime.cs)) — отдельный быстрый
> тик (`PeriodicTimer` 15 c), синхронизирует инстансы с конфигами, тикает блоки, **каждый блок проецируется
> как виртуальное capability-устройство**: анонс `DeviceDiscoveredV1` (выходы типа = capabilities) + выходы →
> `DeviceStateReportV1` (`source=block:{id}`) → бесплатно `/devices`, история/телеметрия (P0-5), зоны, команды.
> **Композиция** = вход блока привязан к capability другого (вирт.) устройства; `DeviceRegistry` = blackboard
> (зеркалится сразу + живёт от подписки `AutomationEngine`). Уставки writable-выходов меняются командой на
> вирт. устройство (`BlockRuntime` слушает `DeviceCommandV1`). **2 примитива:** EWMA-фильтр (эстиматор,
> time-aware) и термостат с гистерезисом (контроллер, writable `temperature_setpoint`) — демонстрируют
> композицию фильтр→контур. **E1:** новый *тип* = C#-класс + регистрация; *инстанс* = чистый конфиг.
> **Авторинг A (типизированные формы):** `control_blocks` CRUD в DbGateway (вирт. DeviceId выводится
> `DeviceIdFactory` при создании) + прокси `BlocksController` (CRUD→DbGateway, `catalog`→AutomationService) +
> WebUI `/blocks` (создание по каталогу с полями параметров и привязкой портов, живой вывод). 8 .NET-проектов +
> `tsc`/lint/26 тестов зелёные. **Не проверено вживую** против RabbitMQ/Mongo.
> **Дальше:** E2 композитные блоки (без кода/рестарта), 3-й примитив (секвенсор полива), DSL-авторинг (B),
> визуальный node-редактор (C, в 1E); E3 скрипты / E4 плагины — позже.

**Проблема.** Правило (1A) — реактивное и без состояния («событие → условие → действие»). Но многое требует
**состояния + периодичности + обратной связи**, при этом каждый такой блок «слишком мал» для отдельного сервиса:
PWM/PID/TPI-термостат, смесительный клапан, вентиляция по CO2, программа полива (секвенсор зон с ET/дождь-
коррекцией), циркадная рампа света, fusion присутствия (motion+door+mmWave с debounce/timeout), фильтры/эстиматоры
(EWMA, dew point, интегратор энергии→кВт·ч), координаторы (load-shedding, агрегатор теплопотребления), интерлоки/
watchdog. Пять классов: **контроллеры · секвенсоры/state-machine · эстиматоры/виртуальные сенсоры · координаторы/
арбитры · интерлоки**.

**Рантайм (зафиксированная развилка W1 — шина как субстрат связывания).**
- Блок = stateful-компонент: `Tick(now)` (периодически) и/или `OnInput(signal)` (реактивно); свой быстрый тик
  (`PeriodicTimer` ~10–30 c) отдельно от минутного rule-scheduler.
- **Каждый блок проецируется как виртуальное capability-устройство** (выходы `demand`/`setpoint`/отфильтрованные
  сигналы = capability-состояния) → бесплатно UI (`/devices`), история (P0-5), зоны (P0-3), команды.
- **Композиция = блок читает capability другого (виртуального) устройства.** Граф связей возникает сам; отдельный
  дата-флоу-движок не нужен. `DeviceRegistry` = blackboard живых сигналов.
- Выходные команды → `DeviceCommandV1` с `source=block:{id}` → P0-5 атрибуция + телеметрия + объяснимость (1F).
- Хостится в AutomationService (→ «Automation & **Control** Service»); конфиги — новая коллекция `control_blocks`
  (CRUD/прокси по паттерну `automations`), грузит `RefreshLoop`. **Не новый сервис, не переписывание** — переиспользует
  реестр/шину/планировщик/загрузку из 1A.

**SDK (Block):** `IBlockType` (TypeId + схема портов/параметров + фабрика) + `IBlock` (`Tick`/`OnInput`) +
`IBlockContext` (`Read(input)` / `Emit(output)` / `State<T>()` / `Now` / `Log`). Инстанс + биндинги портов — документ
(как `AutomationRule`); обратные связи допустимы (сэмплируются по тикам).

**Лестница расширяемости (зафиксировано: ставим E1→E2 сейчас, E3/E4 позже).**
- **E1. Built-in типы** — C#-класс `IBlock` + регистрация в каталоге; first-party, in-process (offline-критичный путь). Новый *тип* = передеплой, инстансы — чистый конфиг.
- **E2. Композитные блоки** — декларативный документ, склеивающий примитивы в подграф; **без кода и рестарта** (золотая середина, ~80% нужд).
- **E3. Скриптовые блоки** — песочница `Tick(ctx)` с урезанным API; на .NET — **чисто-managed** движки без нативных зависимостей (Jint/MoonSharp) с лимитом времени, либо Roslyn для доверенного. Скрипт видит только `ctx` (read/emit/state), никакого IO.
- **E4. Плагины-блоки** — внепроцессные (в т.ч. Python/ML) по манифесту Эпика [1C](#эпик-1c-integration-sdk--внепроцессные-плагины-поверх-шины), resource-aware включение.

**Авторинг (зафиксировано: A+B сейчас, визуальный C — в 1E). Один канонический документ блок-графа; форма/DSL/визуал — виды над ним.**
- **A. Типизированные формы** — по схеме типа блока UI рисует форму (выбор типа, параметры, привязка входов/выходов к device+capability). Расширение нынешнего rule-builder; дёшево, сразу полезно.
- **B. Текстовый DSL (декларативный)** — канон сериализации; diff-абельно, ревьюится в git, дружелюбно к LLM-авторингу (Фаза 2); пайп `|>` выражает композицию (`device(...).temperature |> ewma(tau=300s)`).
- **C. Визуальный node/flow-редактор** (Loxone/Node-RED/Homey Advanced Flow) — флагман UX, рендерит/редактирует тот же документ; территория **Эпика 1E** (React Flow).

**DoD:** существует Block-SDK + каталог + рантайм; ≥2–3 примитива (фильтр-эстиматор, термостат-контур, секвенсор полива)
работают как виртуальные устройства (видны в UI, пишут историю/телеметрию, принимают уставку командой); композиция
двух блоков (фильтр → контур) работает через capability-связывание; новый тип блока добавляется без рестарта на уровне
E2 (композит) и конфигом-инстансом на уровне E1.

---

# Фаза 2 — Интеллект и расширения (среднесрочно)

**Цель фазы:** дом начинает «подсказывать» через **обучающуюся автоматизацию на ML.NET**, умнее распознаёт
устройства и даёт читаемый центр активности; вводятся роли пользователей. **Полная безопасность доступа +
умные замки и тяжёлый ML (видео/голос) перенесены в Фазу 3.** План согласован с владельцем (ревью, 2026-06-01).

> **Чем наш AI отличается от Home Assistant (важно для позиционирования).** HA делает **LLM-
> ассистента** (голос + помощь в авторинге правил) и сознательно НЕ делает обучение на истории.
> Domovoy делает **обучающуюся автоматизацию** (ML на телеметрии → детерминированные предложения) —
> это незанятая ниша и комплементарная HA вещь. LLM у нас уместен НЕ в контуре управления, а как
> слой авторинга/объяснений. См. [`positioning_ru.md`](positioning_ru.md). Локальный голос —
> интегрировать готовый стек, а не строить с нуля (HA эту тему уже закрыл).

> **Зафиксировано с владельцем (ревью плана Фазы 2):**
> - **ML = блок 1H с обучаемой логикой на ML.NET** (не Python, не отдельный пайплайн): входы = capability-фичи
>   → выход = уставка/прогноз; переиспользует рантайм блоков, виртуальные устройства, историю, Shadow,
>   актуацию 1D. Python — дверь под видео/голос (Фаза 3).
> - **Основной вывод ML — уставки** (модель предлагает целевое значение, детерминированный контур исполняет,
>   безопасный пол вето сверху — принцип 1), плюс комплементарные **ML→предложения правил** для дискретных
>   автоматизаций (сохраняют ров объяснимости 1F).
> - **Апрувится версия модели + уровень полномочий, НЕ отдельные выходы.** Живой поток управляется
>   гардрейлами (клампы пола, мониторинг дрейфа, авто-демоут в Shadow); доверие растёт стадийно
>   **Shadow → Bounded-Active (узкая клампленная полоса) → Full**.
> - **Безопасность в Фазе 2 — только модель ролей** (без логина/токенов/сессий). Полная авторизация/TLS/аудит
>   и **умные замки** → Фаза 3 (замки нельзя без auth; auth мешает разработке сейчас).
> - **Реестр моделей** — метаданные в Mongo (`ml_models`), артефакт на volume/GridFS; тренировка —
>   периодический .NET-job + запуск вручную.

> **Пререквизит — Фаза 1.5 (верификация рантайма, S–M) — 🚧 в работе.**
> - ✅ **Интеграционный data-path тест** против **реальных RabbitMQ + Mongo** ([`tests/Domovoy.IntegrationTests`](../../tests/Domovoy.IntegrationTests/),
>   Testcontainers): discovery+state по шине → `EventInterceptor` → `capability_devices`/`device_events`/`sensor_readings`;
>   запись `mode_change` (1G); round-trip BSON новых моделей (`ControlBlock`/`HomeState`). Закрывает P0-4
>   (адаптер→шина→Mongo) и риск Mongo-сериализации. Тесты используют только локальные контейнеры (offline-инвариант).
> - 🚧 **Остаётся:** offline-smoke в CI (прогон на internal-сети без интернета) + полный живой 3-оконный прогон по
>   [`runbook.md`](../runbook.md) с реальными адаптерами/Zigbee, срабатыванием правил (1A/1F), блоками (1H/1D),
>   супервизором плагинов (1C) и SignalR-плечом.
> - **DoD:** каждый ключевой поток зелёный против реальной инфры; offline-smoke в CI.

### Эпик 2A. ML-субстрат на .NET (ML.NET) ✅
- Тренировочный .NET-компонент (новый `Domovoy.MlService` или job в AutomationService): читает event-log/телеметрию (P0-5/1B), обучает ML.NET-модель (старт — регрессия/forecasting под уставку), сохраняет артефакт + регистрирует версию в `ml_models` (Mongo). Тренировка по расписанию + вручную.
- SDK-хук в рантайме блоков 1H: **ML-блок** (встроенный E1-тип, напр. `ml_setpoint`) грузит модель по `modelId`/версии и делает inference на тике; наполняет `decisionId`/`triggerSource=ml`.
- **DoD:** есть зарегистрированная версия модели (метаданные в Mongo, артефакт на volume); ML-блок её грузит и выдаёт значение; тренировка запускается по расписанию и вручную.

> **✅ Реализовано (Epic 2A — v1).** Контракт [`MlModel`](../../src/Common/Domovoy.Contracts/Ml/MlModel.cs)
> + **реестр в DbGateway**: [`MlEndpoints`](../../src/Gateway/Domovoy.DbGateway/Endpoints/MlEndpoints.cs) —
> `ml_models` (метаданные + артефакт inline), `GET /api/ml/models[/latest]`, `GET …/{id}/artifact`,
> `POST /api/ml/models`. **AutomationService** (без Mongo, всё по HTTP): [`MlTrainer`](../../src/Services/Domovoy.AutomationService/Ml/MlTrainer.cs)
> (ML.NET SDCA-регрессия время→значение = «обученное расписание»), [`MlModelService`](../../src/Services/Domovoy.AutomationService/Ml/MlModelService.cs)
> (грузит/кэширует последнюю модель, синхронный `TryPredict` для тика блока), [`MlTrainingService`](../../src/Services/Domovoy.AutomationService/Ml/MlTrainingService.cs)
> (периодическая тренировка + загрузка) + `POST /api/ml/train` (вручную). **ML-блок** `ml_setpoint`
> (E1-тип в каталоге 1H) — на тике предсказывает уставку и эмитит `temperature_setpoint` (с safety-clamp).
> Прокси `MlController` (models→DbGateway, train→AutomationService). WebUI `/models` (список + «Train now»).
> **Решение по схеме:** v1 обслуживает одну последнюю модель (выбор модели на инстанс — в 2C; `Params` блоков
> только числовые). **Тесты:** 2 юнит (тренер: fit+round-trip, cold-start guard); 17/17 .NET-тестов зелёные.
> 10 .NET-проектов + WebUI `tsc`/lint/26 тестов. **Дальше:** 2B (термостат shadow→bounded→full + scorecard),
> модель-per-инстанс (2C), фичи из 2D-архетипа.

### Эпик 2B. ML-уставка-блок: shadow → bounded → full ⬜ (флагман)
- Первый кейс — **ML-термостат/уставка** (упреждающая целевая температура из истории + присутствие/режим); исполняет детерминированный контур (модель не крутит актуатор напрямую).
- **Стадии полномочий:** Shadow (логирует, не актуирует) → Bounded-Active (двигает уставку в узкой клампленной полосе вокруг базовой) → Full; промоут между стадиями = апрув; безопасный пол клампит на всех стадиях; при дрейфе/отсутствии модели — безопасный дефолт + авто-демоут в Shadow.
- **DoD:** виден backtest-scorecard (предсказание vs факт) + Shadow-сравнение без команд; после апрува блок управляет уставкой в рамках стадии под клампами пола.

### Эпик 2C. Очередь предложений + апрув ⬜
- Единый UI: промоут ML-блоков (Shadow→Active со scorecard/провенансом) + ML-**предложения правил** (`Proposed`, валидируются реплеем 1F — объяснимый дискретный путь, напр. «свет по присутствию»).
- **DoD:** пользователь видит очередь, смотрит обоснование (реплей/scorecard, модель/версия/`decisionId`), апрувит/реджектит; активированное действие трассируется к `decisionId`.

### Эпик 2D. Обнаружение + семантическая типизация устройств ✅
- Классификатор архетипа (свет/термостат/датчик/замок/…) из набора capability + метаданных Z2M (`definition`/модель); **native-тип приоритетен**; поле `archetype` в read-модель `capability_devices`; UI использует для иконок/группировки/контролов; ручная коррекция. Эвристики v1 → ML.NET-классификатор (эмпирически из поведения) позже.
- **DoD:** устройства из Z2M/Native получают выведенный архетип; UI и предложения автоматизаций его используют.

> **✅ Реализовано (Epic 2D — v1, эвристики).** Открытый словарь [`DeviceArchetypes`](../../src/Common/Domovoy.Contracts/Devices/DeviceArchetypes.cs)
> (light/switch/thermostat/climate_sensor/motion/contact/lock/valve/energy_meter/sensor/control_block/unknown).
> Эвристический [`DeviceClassifier`](../../src/Gateway/Domovoy.DbGateway/Services/DeviceClassifier.cs) (по набору
> capability + adapterSource + model; **явная модель/native-тип приоритетны**; устойчив к `JsonElement` writable
> после транспорта по шине). На discovery `EventInterceptor` пишет `AutoArchetype` (пересчёт на каждый ре-анонс),
> ручной override — `Archetype` (не затирается). `PUT /api/capability-devices/{id}/archetype` (null = вернуть авто)
> + прокси. WebUI: эффективный архетип (`override ?? auto`) задаёт категорию/иконку (откат к старой эвристике при
> unknown), чип типа + селектор override в drawer. **Тесты:** 12 юнит (классификатор) + интеграционный assert на
> discovery против реального Mongo (поймал баг writable-over-bus). 10 .NET-проектов + WebUI `tsc`/lint/26 тестов
> зелёные. **Дальше:** ML.NET-классификатор; использование архетипа в ML-предложениях (2C).

### Эпик 2E. Модель ролей ⬜ (без enforcement)
- Локальные пользователи/роли/права (манифест плагина уже несёт `permissions`); назначение ролей. **Без** логина/токенов/сессий — enforcement в Фазе 3.
- **DoD:** CRUD пользователей/ролей + привязка прав; модель готова к будущему enforcement; в dev ничего не блокирует.

### Эпик 2G. Центр активности (события + логи + уведомления) ✅ (v1, без каналов доставки)
- Переосмысление пустого `/logs`: единый **читаемый/искомый/фильтруемый** вид — device-события (`/api/events`), история автоматизаций (`auto_history`), ops-логи (Serilog Mongo-sink `ops_logs`, **раздельно** с доменными по P0-5, объединение на уровне запроса/UI), алерты. Фильтры по источнику/severity/устройству/времени. Каналы доставки (push/telegram/email) — под-часть, ближе к концу.
- **DoD:** один экран отвечает «что произошло и почему», ищется/фильтруется; аномалия датчика (из 2B) и сбой автоматизации видны; ≥1 канал доставки.

> **✅ Реализовано (Epic 2G — v1).** **Ops-логи в Mongo:** Serilog Mongo-sink (`Serilog.Sinks.MongoDB` 5.4,
> driver-2.x совместимый) в общем [`SerilogBootstrap`](../../src/Common/Domovoy.Common/Logging/SerilogBootstrap.cs)
> — env-gated `SERILOG_MONGO_URI` (best-effort, try/catch), пишет в capped-коллекцию `ops_logs` (100 МБ /
> 200k док.), раздельно с доменным event-log (P0-5). Env проставлен всем .NET-сервисам в compose. **Единый
> фид:** [`ActivityEndpoints`](../../src/Gateway/Domovoy.DbGateway/Endpoints/ActivityEndpoints.cs) —
> `GET /api/activity?source=&severity=&deviceId=&from=&to=&q=&limit=` мерджит на уровне запроса три источника
> (`device_events` + `auto_history` + `ops_logs`) в единый `ActivityEntry` (timestamp/source/severity/title/
> detail/deviceId/kind/service), сортировка/фильтры/поиск; прокси в ApiGateway. **WebUI:** пустой `/logs`
> переделан в «Activity» — фильтры по источнику (Devices/Automations/System) и severity, поиск, окно (1ч/24ч/7д),
> цветовая полоса severity, резолв deviceId→имя. 10 .NET-проектов + WebUI `tsc`/lint/26 тестов + 15 .NET-тестов
> зелёные. **Дальше:** каналы доставки (push/telegram), аномалии из 2B.

### Эпик 2H. LLM — коннекторы-заглушки ⬜
- Точка расширения под будущий LLM (NL-авторинг → `Proposed`-правило через 2C; объяснения «почему» прозой поверх 1F), **без** реальной модели (нет железа — разработка на ноутбуке).
- **DoD:** интерфейс/коннектор + фича-флаг есть; реальная интеграция — позже.

> **Порядок (реком.):** Фаза 1.5 → **2D + 2G** рано (фундамент UX/данных, низкая зависимость) + **2A**
> параллельно (пайплайн ML) → **2B → 2C** дозревают по мере накопления истории (ров «обучающаяся
> автоматизация») → **2E** (мелкий, в любой момент) → **2H** в конце.

---

# Фаза 3 — Автономный дом (долгосрочно, концепт)

**Цель фазы:** дом управляется преимущественно сам, с речевым интерфейсом и видеоаналитикой. Уровень — направления и открытые вопросы, без детализации задач.

- 🔬 **Видеоаналитика** (распознавание автономеров/лиц) как отдельная GPU-подсистема. На шину идут только **события** («номер X у ворот», «движение в зоне Y»), не сырое видео. Триггерит сценарии доступа.
- 🔬 **Локальный голосовой ассистент** как edge-плагин — **интегрировать готовый локальный стек (STT/intent/TTS), а НЕ строить с нуля.** HA уже сделал локальный LLM-голос хорошо (Piper/Ollama, streaming TTS), это догоняющий клон, если делать самим. На шину — интенты/команды и ответы; низкая задержка → строго локально.
- 🔬 **ML в active-режиме** — модели управляют климатом/светом в заданных безопасных границах, продолжая дообучаться; пользователь корректирует, система адаптируется. Безопасный пол (1A) остаётся неотключаемым над ML.
- 🔬 **Matter/Thread** как **additive/сходящийся** стандарт (наряду с основным Zigbee2MQTT), НЕ ранняя ставка: на 2026 Matter «реален, но требует активного управления сетью» — фрагментация Thread-роутеров, межплатформенные баги (см. [`positioning_ru.md`](positioning_ru.md)).
- 🔬 **Энергооптимизация** (учёт тарифов/погоды/прогноза для отопления и полива). Ориентир по подаче данных/UX — Homey Energy (открытый аналог).
- 🔬 **Полная безопасность доступа (перенесено из Фазы 2).** Локальная авторизация (логин/токены/сессии поверх ролей из 2E), TLS/auth на MQTT и шине, аудит — и только затем **умные замки и контроль доступа** (домофон, открытие по событию). Делаем позже: auth мешает разработке, замки нельзя без auth.
- 🔬 **Облачные интеграции внешних устройств** (робот-пылесос, газонокосилка) как опциональные плагины 1C сверх заглушки; при отсутствии интернета плагин не активен (offline-first).
- 🔬 **Углубление ML:** counterfactual loop-replay (прогон контура с предложенными параметрами по истории) для строгой валидации непрерывных ML-выходов; ML.NET-классификатор типизации устройств (из 2D-эвристик).

---

## Сводная карта фаз

| Фаза | Горизонт | Ключевой результат | Главные компоненты |
|---|---|---|---|
| **0. Фундамент** | ✅ заложен | ядро пригодно к росту | контракт, capability-модель, зоны, event-log (Mongo TS), снятие auth-трения + чистка |
| **1. Автоматизация** | ✅ функц. закрыта | дом работает по сценариям; копятся данные; действия объяснимы | AutomationService (1A ✅), режимы дома+присутствие (1G ✅), платформа данных телеметрии (1B ✅), объяснимость+реплей (1F ✅), control blocks (1H ✅ E1), climate/heating/irrigation контуры (1D ✅), Integration SDK (1C ✅ супервизор), визуальный flow-редактор (1E ✅) |
| **1.5 Верификация** | 🚧 в работе | стек проверен вживую | ✅ интеграционный data-path тест (P0-4) против реальных RabbitMQ/Mongo (Testcontainers); 🚧 offline-smoke в CI + живой 3-оконный прогон |
| **2. Интеллект** | 🚧 начата | дом подсказывает; умнее распознаёт устройства; читаемый центр активности | типизация устройств (2D ✅), центр активности (2G ✅), ML-субстрат на ML.NET (2A ✅), ML-термостат shadow→bounded→full (2B), очередь предложений (2C), роли (2E), LLM-заглушки (2H) |
| **3. Автономность** | долгосрочно | самоуправление + голос + видео + доступ | видеоаналитика, голосовой ассистент, active-ML, Matter/Thread, **полная безопасность + умные замки** (из Ф2), облачные плагины |

## Сквозные направления (на всех фазах)

- **Наблюдаемость:** Prometheus-метрики на каждый сервис/плагин, health/readiness; операционные логи через Serilog (сейчас консоль; опционально Mongo-sink) — отдельно от доменного event-log. **Трассировка решений** автоматизаций/ML (какое правило/модель и по какому триггеру совершило действие) — часть доменных данных, питает объяснимость (Эпик 1F), НЕ операционные логи.
- **Тестирование:** интеграционные тесты на сквозные потоки (discovery, команда, state, срабатывание правила) — растить покрытие с Фазы 0. **Offline-smoke:** тест «всё ядро (discovery/команда/state/правило) работает при отключённой внешней сети» — гарантия инварианта offline-first (принцип 2), а не декларация.
- **Устойчивость:** реконнект к шине, восстановление состояния из БД, graceful degradation при недоступных зависимостях.

## Принятые решения (зафиксировано с владельцем)

1. **Железо — homelab** (мини-ПК → полноценный ПК → стойка), не промышленное; сервер совмещён с хранилищами и др. задачами. Отсюда **resource-aware модульность**: тяжёлые плагины/компоненты включаются при наличии мощностей, иначе — базовый функционал.
2. **Телеметрия и доменный event-log — MongoDB time-series** (единая БД; отдельный TSDB не вводим). Mongo изначально выбран как хранилище объектов с поддержкой time-series.
3. **Брокер — RabbitMQ** (закрывает и шину, и MQTT-брокер; NATS не нужен). **Event-log пишем в Mongo** (доменные данные для ML/аудита), Serilog — только для операционной диагностики.
4. **Авторизация — локальная и отложенная.** JWT на стадии разработки убираем как мешающий артефакт; позже — локальная авторизация «поверх» шлюзов/UI для контроля пользователей и внешних процессов. **Вся система обязана стабильно работать локально без внешней сети**; облачные интеграции — опциональны.
5. **ML — через ML.NET и как блоки 1H, не Python-пайплайн (ревью плана Фазы 2).** Аналитический ML (уставки, прогноз, аномалии, типизация) — на ML.NET, переиспускает рантайм control-blocks; модель — это блок (вход=фичи → выход=уставка). Python — только для будущих видео/голоса (Фаза 3). **Апрувится версия модели + уровень полномочий, не отдельные выходы**; живой поток под клампами безопасного пола + стадийный выкат Shadow→Bounded-Active→Full. **Полная безопасность доступа + умные замки перенесены в Фазу 3** (auth мешает разработке сейчас, замки нельзя без auth). Перед Фазой 2 — **Фаза 1.5** (живая верификация рантайма).

---
*Живой документ. Обновляется по мере прохождения фаз; детализация дальних фаз растёт по мере приближения.*
