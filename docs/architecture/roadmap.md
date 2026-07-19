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
> (устарело — историческая 6-сервисная архитектура),
> [`memory-bank/marketResearch2026.md`](../../memory-bank/marketResearch2026.md) (deep-research рынка
> умного дома 2025–2026 — источник перегруппировки forward-фаз 3/4/5, см. решение №6 в конце документа),
> [`docs/deployment_plan_ru.md`](../deployment_plan_ru.md) (план боевого развёртывания малого дома владельца:
> требования к железу + предусловия карты до запуска + правки compose + миграция с HA; вынесен из карты как
> операционная активность, решение №7).

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
> **✅ Хвост (2026-07-06):** термостат получил **режим heat/cool/heat-cool** (числовой param `mode`: 0 нагрев —
> обратно совместимо, 1 охлаждение — инверсия demand на новый выход `cool_demand`, 2 heat-cool — нагрев ниже
> `setpoint`, охлаждение выше `coolSetpoint`, нейтральная мёртвая зона между); новый блок **`sun_gate`**
> (наружное освещение по солнцу — `on_off` = «темно» dusk-to-dawn с offset-опережением, поверх `SunCalculator`,
> офлайн). Оба — виртуальные устройства (история/телеметрия). 9 юнит-тестов блоков зелёные.
> **Дальше:** многозонный полив как E2-композит (см. 1H E2).

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
> **✅ Хвост (2026-07-06):** **визуализация графа control-blocks** на том же React Flow —
> [`BlockGraph`](../../src/UI/WebUI/src/components/blocks/BlockGraph.tsx): блоки = узлы, input/output-биндинги =
> рёбра к устройствам-источникам/актуаторам; **композиция блок→блок** видна там, где вход одного блока привязан
> к виртуальному устройству другого (blackboard 1H). Переключатель «Список ↔ Граф» на `/blocks`. Очередь апрува
> на предложения — уже в 2C (`/proposals`).

> **✅ Хвост — drag-connect-save редактор блоков (2026-07-08, ветка `epic-1h-e2-composites`).** `BlockGraph`
> из read-only-визуализации стал **интерактивным**: кастомные узлы блок/устройство с хэндлом на каждый
> порт/capability (id хэндла кодирует порт/cap → нарисованная связь однозначно маппится в `PortBinding`, без
> пикера), перетаскивание сохраняет `ControlBlock.Layout {x,y}` (новое поле; DbGateway `PUT` его сохраняет при
> не-layout-правке), двойной клик по ребру снимает привязку, Save шлёт только «грязные» блоки. Вся логика
> связывания — в чистой [`blockGraphModel.ts`](../../src/UI/WebUI/src/components/blocks/blockGraphModel.ts)
> (8 vitest), компонент — тонкая оболочка над React Flow. `PUT /api/blocks/{id}` уже существовал на всех
> слоях. 72 WebUI-теста зелёные. **UI-клик-прогон вживую ещё не делали.**

> **✅ Хвост — модернизация редактора (2026-07-08, обосновано deep-research).** Решение: **остаёмся на линии
> React Flow + тонкий доменный слой** (не мигрируем на Rete/Flume/LiteGraph — их ценность в клиентском
> dataflow-движке, а у нас исполнение серверное в .NET; у Rete продвинутые плагины под CC-BY-NC-SA
> несовместимы с AGPL; GoJS коммерческий). Наш «самописный» слой (кастомные узлы, маппинг связь→binding,
> save/load) — **неотъемлемый доменный клей**, который потребовала бы любая библиотека. Сделано:
> **(1) апгрейд `reactflow` v11 → `@xyflow/react` v12** (MIT): переименование пакета + CSS-путь, `ReactFlow`
> стал named-экспортом, `NodeProps<Node<Data>>`, data-типы узлов переведены `interface`→`type` (v12 требует
> `data extends Record<string,unknown>`) в `BlockGraph.tsx` и `Flow.tsx`; `project()`/мутацию узлов не
> использовали — миграция чистая.
> **(2) авто-раскладка через dagre** (`@dagrejs/dagre`, MIT; elkjs отвергнут — EPL-2.0 копилефт, ломает
> AGPL+commercial): чистая [`computeAutoLayout`](../../src/UI/WebUI/src/components/blocks/blockGraphLayout.ts)
> (LR-раскладка, центр dagre→top-left RF, unit-тест), кнопка «Auto-arrange» кладёт позиции блоков в сохраняемый
> `Layout` (persist + dirty), позиции устройств эфемерны; `fitView` после раскладки.
> **(3) undo/redo** чистым snapshot-редьюсером [`history.ts`](../../src/UI/WebUI/src/components/blocks/history.ts)
> (past/present/future, cap 50, unit-тест): connect/unbind/drag-settle/auto-layout коммитятся как шаги истории;
> кнопки Undo/Redo + `Ctrl+Z`/`Ctrl+Shift+Z`/`Ctrl+Y`; форс-ресинк узлов канвы при undo/redo layout-only шага
> (сигнатура ре-синка по-прежнему игнорирует позицию → 5-с device-poll не перестраивает канву). i18n-строки
> графа (ru/en). 10 новых vitest (история 6 + layout 4) → **82 WebUI-теста + `tsc` + `vite build` зелёные**;
> lint новых файлов чист. **UI-клик-прогон вживую ещё не делали.** Blockly/Node-RED как альтернативная парадигма
> в исследовании не оценены (пробел покрытия) — при желании отдельное мини-исследование.

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
> **✅ Хвост (2026-07-06):** добавлен статус **`BoundedActive`** (стадия между Shadow и Active) — правило
> исполняется, но не чаще `BoundedActiveCooldownSeconds` (throttle частоты актуации = «безопасная граница»
> по темпу; защитные/Active-правила не троттлятся). Оценивается движком и планировщиком; `ActionExecutor`
> держит per-rule last-fire и при попадании в cooldown пишет в историю «throttled», команд не публикует.
> WebUI: статус Bounded в селекторе+диалоге (`/automations`). **Валидация реплеем перед активацией** уже
> закрыта (кнопка «Simulate» на Proposed-предложениях `/proposals` → `/api/replay` кандидата над историей).
> 3 юнит-теста throttle (119 .NET-юнит зелёные).

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
> **✅ Хвост — E2 композиты + DSL (2026-07-06):** композитный блок — **декларативный документ** (без кода/
> рестарта): [`CompositeDefinition`](../../src/Services/Domovoy.AutomationService/Blocks/Composite/CompositeDefinition.cs)
> (узлы-примитивы + внутренние биндинги + внешние порты), [`CompositeBlockType`/`CompositeBlock`](../../src/Services/Domovoy.AutomationService/Blocks/Composite/CompositeBlock.cs)
> — first-class `IBlockType`, тикает дочерние примитивы по внутреннему blackboard (`InternalContext` мостит
> внешние порты к родительскому контексту, состояние namespaced по узлу, внутр. сигналы живут между тиками).
> **DSL (авторинг B)** [`BlockDsl`](../../src/Services/Domovoy.AutomationService/Blocks/Composite/BlockDsl.cs):
> пайп `input(temperature) |> ewma_filter(tau=300) |> thermostat(setpoint=21)` → определение (+ round-trip
> Serialize). Встроенный композит `climate_loop` + из конфига `Automation:Composites` — новый тип = запись
> конфига, компилируется в каталог. Node-редактор (C) закрыт граф-view в 1E; 3-й примитив (секвенсор полива)
> сделан в 1D. 4 юнит-теста (135 .NET-юнит). **Дальше:** ветвящиеся композиты, композит-в-композите, hot-reload
> из DB; E3 скрипты (Jint/MoonSharp) / E4 плагины — позже.

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

**Цель фазы:** дом начинает «подсказывать» через **обучающуюся автоматизацию на ML.NET** — как по
закономерностям, заданным человеком (ML-термостат 2A/2B), так и **найденным системой самостоятельно**
в накопленной истории (движок поиска закономерностей 2F → предложения в очередь 2C); умнее распознаёт
устройства и даёт читаемый центр активности; вводятся роли пользователей. **Полная безопасность доступа +
умные замки и тяжёлый ML (видео/голос) перенесены в Фазу 3.** План согласован с владельцем (ревью, 2026-06-01;
2F добавлен 2026-06-14).

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
> - **Закономерности ищет не только человек, но и система (2F, добавлено 2026-06-14; уточнено 2026-06-18).**
>   Поверх задаваемых вручную моделей — автономный движок поиска закономерностей в истории. Учитель — **любая
>   стационарная не-ML политика** (человек / правило 1A / контур 1H), но **не другая ML-модель** (запрет
>   ML-on-ML). Три типа выхода: **A** правило, **B** ML-уставка-предпочтение, **C** feedforward-аугментация
>   контура (учим установившийся `возмущения→u_ss`, снимаем лаг контроллера; физический лаг — упреждением по
>   прогнозу, Фаза 3). Пайплайн: скрининг MI/Granger → mining правил/деревья/регрессия → валидация реплеем 1F →
>   `Proposed` в очередь 2C. Движок — **только поставщик гипотез**, не актуатор; апрув и стадийный выкат те же.
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
> - ✅ **offline-smoke в CI (2026-07-06):** GitHub Actions [`ci.yml`](../../.github/workflows/ci.yml) —
>   job `build-test` (сборка .NET + WebUI + юнит-тесты `Category!=Infra`, без Docker) + job `offline-smoke`
>   (тесты `Category=OfflineSmoke` против реальных RabbitMQ+Mongo через Testcontainers на loopback — ядро
>   работает без внешней сети в горячем пути, инвариант принципа 2 как проверка). 7 offline-smoke тестов.
> - ✅ **Живой 3-оконный прогон (2026-07-06):** проведён владельцем — discovery + отслеживание реакции
>   элементов, включая реальное Zigbee-устройство.
> - **DoD:** каждый ключевой поток зелёный против реальной инфры ✅; offline-smoke в CI ✅.

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

### Эпик 2B. ML-уставка-блок: shadow → bounded → full ✅ (флагман)
- Первый кейс — **ML-термостат/уставка** (упреждающая целевая температура из истории + присутствие/режим); исполняет детерминированный контур (модель не крутит актуатор напрямую).
- **Стадии полномочий:** Shadow (логирует, не актуирует) → Bounded-Active (двигает уставку в узкой клампленной полосе вокруг базовой) → Full; промоут между стадиями = апрув; безопасный пол клампит на всех стадиях; при дрейфе/отсутствии модели — безопасный дефолт + авто-демоут в Shadow.
- **DoD:** виден backtest-scorecard (предсказание vs факт) + Shadow-сравнение без команд; после апрува блок управляет уставкой в рамках стадии под клампами пола.

> **✅ Реализовано (Epic 2B — ядро стадий).** **Блок-губернатор** `ml_thermostat`
> ([`MlThermostatBlock.cs`](../../src/Services/Domovoy.AutomationService/Ml/MlThermostatBlock.cs), E1-тип в
> каталоге 1H): не крутит актуатор, а **предлагает уставку детерминированному `thermostat`-контуру** командой
> на его writable `temperature_setpoint` (актуация 1D), т.е. контур исполняет под клампами пола (слоистая
> модель). **Стадии** (числовой param `stage`, т.к. `ControlBlock.Params` числовой): 0 Shadow — эмитит
> `proposed_setpoint` для scorecard, но bound-выход **не эмитит → команда не публикуется** (как Shadow-правила
> 1F); 1 Bounded — clamp прогноза в полосу `baseline±band`; 2 Full — clamp только полом. Пол (`SetpointMin/Max`)
> клампит на всех стадиях. **Дрейф-монитор** (скользящая `|proposed−measured|` > `driftThreshold`) и отсутствие
> модели → **авто-демоут в Shadow** + безопасный дефолт; выходы `ml_effective_stage`/`ml_drift` видны как
> вирт-устройство (история/телеметрия P0-5). **Backtest-scorecard** (DoD «предсказание vs факт»): `MlTrainer`
> считает MAE на хронологическом holdout (последнее окно, не виданное при обучении) → новые поля
> `HoldoutMae`/`HoldoutSampleCount` в `MlModel`; `GET /api/ml/backtest?days=` (AutomationService) отдаёт серию
> predicted-vs-actual текущей модели; прокси `MlController`. **WebUI:** `/models` — overlay-график scorecard
> (`ScorecardChart`, recharts) + MAE/RMSE; `/blocks` — селектор стадии Shadow/Bounded/Full + поля
> baseline/band/drift (каталог-форма) + live `ml_effective_stage`/`ml_drift`. **8 ML юнит-тестов** (стадии,
> клампы, полоса, дрейф/no-model демоут, holdout) — 23/23 .NET; WebUI build + lint(мои файлы)/26 тестов зелёные.
> **Решение по объёму:** ядро стадий отгружено на schedule-модели 2A (hour+dow).
> **✅ Хвост — фичи режима (2026-07-06):** **серверный context-join** [`ContextFeatureJoin`](../../src/Services/Domovoy.AutomationService/Ml/Templates/ContextFeatureJoin.cs)
> (as-of join режима из `mode_change`-лога 1G на каждую тренировочную строку) + **context-шаблон**
> [`ContextScheduleRegressionTemplate`](../../src/Services/Domovoy.AutomationService/Ml/Templates/ContextScheduleRegressionTemplate.cs)
> (OneHot(mode) ⊕ hour/dow → SDCA-регрессия), конкурирует с time-only по holdout MAE (2I-селекция) — регистрируется
> только если режим реально снижает ошибку (single-mode → declines). **Паритет train/serve без смены сигнатур:**
> режим глобален, `MlModelService` кондиционирует на **текущий** `HomeModeState.Current` в момент инференса
> (`DbGatewayClient.GetModeTimelineAsync`). `MlModel.Features` template-driven (`"time+mode"`). **Присутствие**
> (per-instance) — остаётся: нужен вход presence в губернатор. 4 юнит-теста (139 .NET-юнит; аддитивно, старые
> зелёные). **Дальше:** очередь апрува/промоута — Эпик 2C (сделан); фича presence. **Не проверено вживую.**

### Эпик 2I. Обобщённые ML-шаблоны + зональный scoping моделей ✅ (Фазы 0–5, 2026-06-28; остаток — мультивариантный пайплайн)
**Мотивация.** 2B-термостат — частный случай «лежащего на поверхности» применения ML. По основной идее
такие реализации должны не рождаться из кода под каждую величину, а собираться из **обобщённых шаблонов**
(по размерности/типу данных), и подбираться по данным. Этот эпик «разворачивает наизнанку» 2A/2B: вытащить
generic-губернатор (он уже почти весь написан в `MlThermostatBlock`) и сделать тренер **реестром шаблонов**.

- **Реестр шаблонов.** Шаблон = `{ targetKind, featureSet, mlTask }`. `MlTrainer` → `IModelTemplate`
  (`Kind`, `CapabilityKind Target`, `Metric`, `Train(LabeledSeries,FeatureSpec)→(artifact,holdoutScore)`).
  Начальный набор по `CapabilityKind` цели: **Number → регрессия** (SDCA, MAE) → Setpoint governor *(есть)*;
  **Boolean → бинарная** (FastTree, AUC) → Toggle governor; **Enum → мультикласс** (LightGbm/SDCA, macro-F1)
  → Selector governor. Ось B (размерность фич) — поле `Features` модели (`time` → `time+mode+occupancy`),
  не отдельный `Kind`. «Подбор модели» = обучить применимых кандидатов и взять лучшего по honest-holdout.
- **Generic-губернаторы вместо классов-копий.** `MlThermostat*` → база `MlGovernorBase`
  (Shadow/Bounded/Full + дрейф + авто-демоут — общие) с hook-методами `Predict/Clamp/Disagreement`;
  `MlSetpointGovernor`/`MlToggleGovernor`/`MlSelectorGovernor` отличаются лишь клампом и смыслом «дрейфа».
  `ml_thermostat` остаётся как **каталожный инстанс** Setpoint-губернатора над temperature; `BlockCatalog`
  регистрирует governor-инстансы из конфиг-списка (по управляемой капабилити), а не классами.
- **Параметры — только числовые (контракты НЕ трогаем).** Губернаторам хватает числовых params
  (`stage`/`band`/`probThreshold`/`minDwellMin`/`maxClassStep`); категориальная привязка течёт через
  `PortBinding.CapabilityId` и дескриптор `Capability` (`Values`/`Min`/`Max`). Полиморфные
  `ControlBlock.Params`/UI **остаются в 2C** — этот эпик их не требует.
- **Per-instance модель через зональную цепочку scope.** Резолюция = пройти цепочку от частного к общему,
  взять **первую существующую** модель: `zone:<id>` → `zone_kind:<Kind>` → `global`. Переиспускаем
  существующую модель зоны (`Zone.Kind` = «тип помещения»; `Zone.ParentZoneId` — на будущее доп-уровни),
  **новых полей в `Zone` нет**. Пример: спальня и гостиная (обе `room`) делят `zone_kind:room`; теплица
  (`greenhouse`) — свою; если у спальни накопилась своя `zone:bedroom` — берёт её. Механизм — generic
  ordered fallback chain (уровни — открытый упорядоченный словарь).
- **Два независимых «своё» у помещения.** Параметры губернатора (baseline/band/stage) — уже per-instance
  в `ControlBlock.Params` (1H). Обученное расписание/уставки — per-zone модель по цепочке.
- **Авто-формирование zone-модели.** Всегда тренируются фолбэки `global`+`zone_kind`; для зоны с достаточными
  данными тренируется кандидат `zone:<id>` и **регистрируется/обслуживает только если честно бьёт фолбэк
  на holdout своей зоны** (порог). Иначе шум — инстанс остаётся на общей. «Отпочкование» по факту расхождения.
- **Фич-локальность (та же цепочка scope-ит входы модели).** Для модели в `zone:Z (kind K)`:
  глобально-окружающие фичи (время, наружная T/погода, режим) — всегда; зонально-локальные — датчики Z и зон
  того же `K` по цепочке (вес по удалённости); **через границу `zone_kind` — жёсткая стена** (домовые датчики
  не кормят теплицу и наоборот, даже при in-sample корреляции). Гостиная→спальня — мягко (тот же `room`),
  дом→теплица — исключение a priori. Вторичная защита от случайной корреляции — holdout-гейт.

> **Фазы (каждая отдельно собирается/тестируется).**
> **Фаза 0** — реестр шаблонов + generic Setpoint governor + зональная цепочка scope + scoped-тренировка с
> авто-промоушеном по holdout. Меняет `MlModel` (+`Scope{Level,Key}`, +`Metric`, +`HoldoutScore`, +`Features`),
> `MlModelService` (мульти-движковый кэш по `(Kind,Capability,Level,Key)` + резолюция цепочкой),
> `AutomationOptions.TrainTargets[]`, `DbGatewayClient`/телеметрию (фильтр по зоне/типу; проверить разметку
> зоны в `sensor_readings`), `IBlockContext.Scope` (из `ControlBlock.ZoneId`+`Zone.Kind`, кэш зон). Поведение
> при одной `global`-модели идентично текущему (8 ML-тестов зелёные) + тесты на резолюцию цепочки и гейт.
> **Фаза 1** — категориальные ряды из `device_events` (state_change) для классификаторов + резолвер
> `CapabilityKind` цели. **Фаза 2** — `schedule_binary` + Toggle governor (порог + min-dwell, дрейф = доля
> расхождений). **Фаза 3** — `schedule_multiclass` + Selector governor (Bounded = соседний класс по `Values`).
> **Фаза 4** — мультивариант/`+context` с фич-локальностью (жёсткая стена `zone_kind`). **Фаза 5** — WebUI
> `/models` (Scope/Metric, scorecard по задаче); `/blocks` — без изменений (scope из зоны инстанса).
>
> **Отношение к 2C.** Этот эпик даёт автоматический выбор модели **по зоне** (не требует параметров). 2C
> остаётся для явного пиннинга версии (`model_version`) и очереди апрува промоутов/предложений — слои
> ортогональны: цепочка выбирает scope/семейство, 2C-пиннинг — конкретную версию внутри него. **Вне scope:**
> авто-авторинг шаблон-кандидата движком (2F); полиморфные params/UI (2C).
>
> **✅ Фаза 0 реализована (2026-06-28, ветка `epic-2b`).** Реестр шаблонов `IModelTemplate`/`ModelTemplateRegistry`
> (+`ScheduleRegressionTemplate` оборачивает `MlTrainer`; `CapabilityKindResolver`); тренер **подбирает** модель
> по типу цели через honest-holdout. Generic-губернатор `MlGovernorBase`/`MlSetpointGovernor`/`MlSetpointGovernorType`
> заменил `MlThermostatBlock`; `ml_thermostat` — каталожный инстанс. **Зональный scope:** `MlModel.Scope{Level,Key}`
> + `ModelScope` (цепочка `zone→zone_kind→global`); `MlModelService` — мульти-движковый кэш по scope + резолюция
> цепочкой; `ctx.ZoneId/ZoneKind` из `ZoneCache`; scoped-тренировка (`TrainTargets` авто из зон, фильтр телеметрии
> по зоне — `/api/telemetry?zoneId` уже был) с авто-промоушеном per-zone по `ZonePromotionMargin`; `MlModel`
> +`HoldoutScore`/`Metric`/`Features`; DbGateway latest-by-scope + версия per (kind,target,scope). **Тесты:** 13 ML
> юнит (вкл. построение цепочки scope и гейт промоушена) — 33/33 .NET; полное решение собирается. **Гейт
> промоушена v1** сравнивает honest-holdout кандидата и фолбэка (прокси; точный бэктест фолбэка на holdout зоны —
> уточнение).
>
> **✅ Фазы 1–5 реализованы (2026-06-28, ветка `epic-2b`).** **Ф1** `fdfe5a1` — `EventLabelEncoder` + маршрутизация
> тренера Number→телеметрия/Boolean·Enum→`device_events`. **Ф2** `73232a6`/`4ba294a` — `schedule_binary`
> (логистич., AUC) + мульти-kind `MlModelService` (отдаёт value/probability/class по `Kind`) + `MlToggleGovernor`
> (`ml_switch`: порог+гистерезис+min-dwell, drift=доля расхождений); `MlGovernorBase` обобщён в хуки
> `EmitBound`/`Disagreement`. **Ф3** `79f3a8c`/`2fd58df` — `schedule_multiclass` (SDCA max-ent, MacroAccuracy) +
> `LabeledSample.Class` + `MlSelectorGovernor` (`ml_selector`: Bounded=соседний класс по `Values`, drift=доля
> промахов). **Ф4 ядро** `7a7e575` — `FeatureLocality` (ambient всегда; своя зона вес 1; соседняя того же
> `zone_kind` вес 0.5; через границу типа — **жёсткая стена**; global=только ambient). **Ф5** `1e87d7b` — WebUI
> `/models` показывает Scope/Metric/Features. **Сетка типов закрыта:** Number→Setpoint, Boolean→Toggle,
> Enum→Selector — новая величина = строка в каталоге, не класс. **Тесты:** 35 ML/governor + смежные, WebUI 26.
> **✅ Хвост — мультивариантный пайплайн v1 (2026-07-06):** контекст-join отгружен (см. 2B) → пайплайн стал
> **мультивариантным**: `ContextScheduleRegressionTemplate` потребляет **ambient-фичу** режима (по `FeatureLocality`
> home mode = ambient → admissible для любой модели/зоны) поверх time и конкурирует по holdout. Это первая фича
> сверх time, текущая через фич-локальность. **Остаток:** произвольные **зональные** admissible-сенсоры
> (same-kind sibling с down-weight) в тренировке+serving — нужен per-instance ввод фич в инференс + живая
> валидация против skew (Фаза 1.5); descriptor-based `CapabilityKindResolver` для авто-тренировки enum-целей.

### Эпик 2C. Очередь предложений + апрув ✅ (2026-07-04, ветка `epic-2b`)
- Единый UI: промоут ML-блоков (Shadow→Active со scorecard/провенансом) + ML-**предложения правил** (`Proposed`, валидируются реплеем 1F — объяснимый дискретный путь, напр. «свет по присутствию»).
- **DoD:** пользователь видит очередь, смотрит обоснование (реплей/scorecard, модель/версия/`decisionId`), апрувит/реджектит; активированное действие трассируется к `decisionId`.

> **План реализации (зафиксировано 2026-06-27, после 2B; код не начат).** Единая коллекция **`proposals`
> в DbGateway** (источник истины по паттерну `automations`/`ml_models`; та же коллекция позже наполняется
> движком 2F). Один контракт `Proposal` ([`Domovoy.Contracts/Proposals/Proposal.cs`]) с дискриминатором
> `Kind`, покрывающий **три потока апрува**, технически готовых в 1F/2B:
> - **`rule`** — `Proposed` `AutomationRule` → `Active`; обоснование = **реплей 1F** (`POST /api/replay`:
>   когда бы сработало, lift); апрув ставит правилу статус `Active`.
> - **`block_promotion`** — ML-блок `stage` 0→1→2 (Shadow→Bounded→Full); обоснование = **scorecard 2B**
>   (`HoldoutMae`/RMSE/версия, overlay `ScorecardChart`); апрув патчит `Params["stage"]` блока.
> - **`model_selection`** — привязка версии модели к инстансу блока; апрув патчит `Params["model_version"]`.
>
> **Выбор модели на инстанс (снимает блокер 2A/2B без нарушения инварианта):** `ControlBlock.Params`
> числовой-only → версию кодируем как **числовой param `model_version`** (0/нет = latest, текущее поведение);
> `MlModelService` резолвит конкретную `Version` вместо latest; `MlSetpointBlock`/`MlThermostatBlock` читают
> param и пробрасывают `DecisionId` модели в `triggerSource=ml`-команды (замыкает трассировку DoD).
>
> **Апрув — операция в DbGateway** (владеет всеми тремя коллекциями): мутация цели + перевод предложения в
> `Approved` атомарны в одной БД; стампит `DecisionId` в применённую цель; AutomationService подхватывает
> через `RefreshLoop`. Reject цель не трогает. **Новый сервис не вводим.**
>
> **Слои:** (1) контракт `Proposal`; (2) DbGateway `ProposalsEndpoints` (`GET ?status=&kind=`, `POST`,
> `POST /{id}/approve`, `/reject`) + side-effects по `Kind`; (3) ApiGateway `ProposalsController` (прокси
> `db-gateway`, калька `AutomationsController`); (4) AutomationService — резолв модели по `model_version` +
> проброс `DecisionId`; (4.5) **минимальный ML→rule предложитель** (по паттерну `MlTrainingService`:
> BackgroundService + ручной `POST /api/proposals/suggest`, читает `device_events` по HTTP) — **сознательно
> один тип паттерна** (ко-встречаемость переходов, напр. «присутствие после заката → свет в пределах T»,
> пороги support/confidence), **явная заглушка-предтеча 2F** (полную воронку MI/Granger/FDR/rule-mining
> строит 2F; здесь — одна эвристика → очередь, кандидат всё равно валидируется реплеем 1F перед апрувом);
> (5) WebUI `/proposals` (rule-поток через реплей + block_promotion через scorecard + model_selection;
> `/blocks` промоут ML-блока идёт через очередь, не прямым редактированием `stage`); (6) тесты (DbGateway
> approve side-effects, resolve версии модели, WebUI очередь) + roadmap 2C ✅.
>
> **Переиспользуется (не пишем заново):** реплей 1F, scorecard/backtest 2B (`GET /api/ml/backtest`,
> `HoldoutMae`, `ScorecardChart.tsx`), `PUT /api/automations/{id}/status`, прокси-инфраструктура HttpClient.
> **Вне scope 2C:** полноценная генерация предложений (поиск закономерностей) — **Эпик 2F**; enforcement прав
> на апрув — роли 2E / auth Фазы 3. **Инвариант:** ни один кандидат не активируется без человека (принцип 1);
> предложитель — только поставщик гипотез, не актуатор.

> **✅ Реализовано (Epic 2C, 2026-07-04, ветка `epic-2b`).** Единый контракт
> [`Proposal`](../../src/Common/Domovoy.Contracts/Proposals/Proposal.cs) (дискриминатор `Kind`
> = `Rule`/`BlockPromotion`/`ModelSelection`; `Status` Proposed→Approved/Rejected; `DecisionId` штампуется при
> апруве) → коллекция **`proposals` в DbGateway**. **Слой 2:** [`ProposalsEndpoints`](../../src/Gateway/Domovoy.DbGateway/Endpoints/ProposalsEndpoints.cs)
> (`GET ?status=&kind=`, `POST`, `POST /{id}/approve`, `/{id}/reject`); апрув применяет side-effect по типу
> ([`ProposalApplication`](../../src/Gateway/Domovoy.DbGateway/Endpoints/ProposalsEndpoints.cs) — вынесен для
> тестируемости): **rule** → правило `Active`, **block_promotion** → патч `Params["stage"]`, **model_selection**
> → патч `Params["model_version"]`; мутация цели + перевод предложения в `Approved` в одной БД (новый сервис не
> вводили), reject цель не трогает. **Слой 3:** прокси [`ProposalsController`](../../src/Gateway/Domovoy.ApiGateway/Controllers/ProposalsController.cs)
> (очередь→db-gateway, suggest→automation-service). **Слой 4 (выбор модели на инстанс):** числовой param
> `model_version` (0 = latest) пробрасывается через governor-хуки (`MlGovernorBase`/setpoint/toggle/selector)
> в предиктор; [`MlModelService`](../../src/Services/Domovoy.AutomationService/Ml/MlModelService.cs) резолвит
> pinned-версию (lazy-load pending-пина в фоновом refresh; до загрузки — фолбэк на latest scope), снимает
> блокер 2A/2B без нарушения числового инварианта `Params`. **Слой 4.5 (ML→rule предложитель):**
> [`RuleSuggester`](../../src/Services/Domovoy.AutomationService/Services/RuleSuggester.cs) — BackgroundService
> + `POST /api/proposals/suggest`; майнит **одну** закономерность из event-log (сенсор→**человеческое**
> `on_off` в окне; учитель только `triggerSource=user` — без ML-on-ml/self-fulfilling; support/confidence,
> дедуп против правил+очереди), кладёт `Proposed`-правило + `Proposal` в очередь — **явная заглушка-предтеча 2F**
> (полную воронку MI/Granger/FDR строит 2F). **Слой 5 (WebUI):** страница `/proposals` (очередь, Pending/All,
> Approve/Reject, «Simulate» rule-предложения через реплей 1F, «Run proposer»); `/blocks` — промоут ML-блока
> идёт **через очередь** («Promote» ставит `block_promotion`), не прямым редактированием `stage`. **Тесты:**
> 6 юнит на майнинг (`RuleSuggester.Mine`: support/confidence, окно, non-user-исключение, self-исключение) +
> 1 на проброс `model_version` в предиктор — 65/65 оффлайн .NET; 4 infra-теста approve side-effects против
> реального Mongo (`ProposalApprovalTests`, Docker-gated); WebUI build+lint+26 тестов зелёные. **Переиспользовано:**
> реплей 1F, scorecard 2B, `PUT /api/automations/{id}/status`, прокси-HttpClient. **Не проверено вживую** против
> RabbitMQ/Mongo (майнинг/апрув на реальном потоке; infra-тесты требуют Docker). **Дальше:** 2F (движок поиска),
> роли 2E на апрув.

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
> зелёные.
> **✅ Хвост (2026-07-06):** **ML.NET-классификатор** [`ArchetypeClassifier`](../../src/Services/Domovoy.AutomationService/Ml/ArchetypeClassifier.cs)
> (мультикласс SDCA maximum-entropy над multi-hot сигнатурой capability + флаг writable-`on_off`) — обучается на
> популяции устройств (лейбл = эффективный архетип, override > эвристика) и обобщает на невиданные сигнатуры.
> **Advisory-раннер** [`ArchetypeAdvisor`](../../src/Services/Domovoy.AutomationService/Ml/ArchetypeAdvisor.cs) +
> `POST /api/ml/classify-archetypes` (прокси `MlController`) — выявляет **расхождения** (устройства, которые модель
> типизировала бы иначе → кандидаты на пересмотр); не мутирует read-модель (как 1F «предлагай, не действуй»).
> WebUI `/models`: кнопка «Классифицировать» + список расхождений. **Архетипы в предложениях (2F):** `DeviceSnapshot`
> обогащён capabilities+архетипом; DiscoveryEngine аннотирует rationale парой `[motion → light]`. 4 юнит-теста
> классификатора (124 .NET-юнит). **Дальше:** обучение на override-корректировках как отдельный сигнал.

### Эпик 2E. Модель ролей ✅ (без enforcement)
- Локальные пользователи/роли/права (манифест плагина уже несёт `permissions`); назначение ролей. **Без** логина/токенов/сессий — enforcement в Фазе 3.
- **DoD:** CRUD пользователей/ролей + привязка прав; модель готова к будущему enforcement; в dev ничего не блокирует.

> **✅ Реализовано (Epic 2E — модель, без enforcement).** **Контракт** `Domovoy.Contracts/Security/`:
> [`WellKnownPermissions`](../../src/Common/Domovoy.Contracts/Security/Permissions.cs) — **открытый** словарь
> прав (`devices.view/control`, `zones/modes/automations/blocks/models/plugins/users.manage`,
> `proposals.approve`, `activity.view`, `system.admin`), открыт как capability-модель (плагин может запросить
> право вне списка — манифесты уже несут `Permissions` строками); [`Role`](../../src/Common/Domovoy.Contracts/Security/Role.cs)
> (имя + описание + `IsBuiltIn` + список прав) и [`User`](../../src/Common/Domovoy.Contracts/Security/User.cs)
> (identity + `RoleIds` + `Enabled`, **без пароля/логина/сессии** — аутентификация в Фазе 3). **DbGateway** —
> источник истины: [`RolesEndpoints`](../../src/Gateway/Domovoy.DbGateway/Endpoints/RolesEndpoints.cs)
> (`/api/roles` CRUD + `GET /permissions` = словарь для UI; built-in роль нельзя удалить, права редактируемы;
> при удалении роли она снимается со всех пользователей), [`UsersEndpoints`](../../src/Gateway/Domovoy.DbGateway/Endpoints/UsersEndpoints.cs)
> (`/api/users` CRUD), [`SecuritySeeder`](../../src/Gateway/Domovoy.DbGateway/Services/SecuritySeeder.cs)
> (hosted-сервис, идемпотентно засевает встроенные роли **admin/resident/guest**, не затирая правки при
> рестарте). **ApiGateway** — тонкие прокси `RolesController`/`UsersController` (калька `ZonesController`).
> **WebUI** — страница `/users` («Пользователи и роли», вкладки): CRUD пользователей с назначением ролей,
> CRUD ролей с чекбоксами прав, чип «встроенная» + защита от удаления; nav-пункт + i18n (ru/en). **Тесты:**
> 4 офлайн-юнит на модель (встроенные роли/словарь прав: admin = супернабор, guest = read-only, только
> известные права, без дублей) + 2 инфра-теста против реального Mongo (идемпотентность сидера без затирания
> правок; BSON round-trip `User`, Docker-gated). Полное решение собирается (0 ошибок); WebUI `tsc`/lint/build
> + 29 тестов зелёные. **No enforcement** — модель готова, гейтинг запросов на права = Фаза 3 (локальная auth).
> **Дальше:** enforcement поверх ролей + логин/токены/сессии (Фаза 3, перед умными замками).

### Эпик 2F. Движок поиска закономерностей (Pattern Discovery Engine) ✅ (v1 — тип A: дискретные политики)

> Добавлен по итогам обсуждения с владельцем (2026-06-14). **Сдвиг от ручного ML к автономному.** 2A/2B
> исполняют закономерность, которую **указал человек** (ML-термостат: «целевая температура из истории»).
> 2F — это слой, который **сам ищет закономерности** в накопленной истории и формулирует их как
> кандидатов: правило (1A) или ML-блок (1H/2A). Тезис владельца: «умный дом = набор явных или неявных
> закономерностей (темно → включи свет)»; 2F добывает неявные из данных. **Это не автономный актуатор:**
> движок только **поставляет гипотезы в очередь 2C**; активация — стадийная (Shadow→Bounded→Full, 1F/2B)
> под клампами безопасного пола (принцип 1). Всю защитную обвязку 2F переиспользует, ничего нового в контур
> управления не вводит.

> **Концептуальная рамка (зафиксировано; уточнено с владельцем 2026-06-18).** Исходную идею «перебрать все
> датчики × все устройства и найти зависимости» берём как **первичный скрининг**, но переформулируем, чтобы
> не ловить мусор:
> 1. **Учитель = любая стационарная не-ML политика, не только человек.** Состояние устройства меняет человек,
>    **правило 1A** или **контур 1H** (термостат/PID/CO₂-вентиляция/полив). Все три — допустимые источники
>    меток для имитации. **Жёсткий инвариант: нельзя учить модель на выходе другой ML-модели** (петля «A учится
>    на B, который учился на A» → дрейф и самоподтверждение) — ML-выходы (`triggerSource=ml`) из обучающей
>    выборки исключаются. Имитировать *явное* дискретное правило 1:1 смысла мало (оно уже есть) — ценность в
>    обнаружении человеческих **переопределений** правила (правило сработало → человек откатил → предложить
>    лучший порог) и в непрерывных контурах (см. тип C ниже).
> 2. **Учить по переходам, а не равномерно по времени.** Сигнал живёт в редких сменах состояния; это
>    убирает дисбаланс «99% времени ничего не меняется» и кратно дешевле, чем `N×M×T`.
> 3. **Корреляция ≠ причинность.** Свет коррелирует с темнотой, временем и присутствием одновременно;
>    нужны условные метрики (см. Stage 1), иначе получаем уверенные ложные правила.
> 4. **Обратная причинность / петля.** Устройства влияют на датчики (свет→люкс, обогрев→темп). Учитываем
>    направление времени и исключаем сенсоры, которые двигает сам актуатор-выход.
>
> **Три типа того, что добывает 2F** (не один — у выходов разные метки, риски и подготовка данных):
>
> | Тип | Учитель / источник | Метка (label) | Главный риск | Выход-предложение |
> |---|---|---|---|---|
> | **A. Политика** (дискрет) | человек / правило 1A | действие вкл/выкл | корреляция≠причинность, конфаундеры | новое правило (1A) |
> | **B. Предпочтение-уставка** (непрерыв.) | человек меняет цель | целевое значение | дрейф предпочтений | ML-setpoint блок (2B) |
> | **C. Feedforward / inverse-plant** (непрерыв., **физика**) | контур 1H + отклик объекта | установившийся уровень актуатора `u_ss` | closed-loop identifiability | feedforward-аугментация контура |
>
> Тип **A/B** = «выход определяется политикой» (имитация). Тип **C — осознанное моделирование физики:** для
> контура (PID-термостат/тёплый пол) учим **установившийся map** `g: (возмущения: t°улицы, солнце, присутствие,
> цель) → u_ss`, чтобы подать его как **feedforward**: `u(t) = u_ss (мгновенно) + PID(e) (трим остатка)`.
> Интегратор не разгоняется с нуля → уходит **лаг контроллера и перерегулирование** (промышленно — «PID с
> feedforward / gain scheduling»). Важно: это **не имитация контроллера** (регрессия выхода PID на его входы
> просто вернёт те же коэффициенты, с лагом), а инверсия объекта. C и B композируются: B говорит «целься в 22°»,
> C — «для этого при −5° на улице открой клапан на 60% сразу». PID остаётся слоем disturbance-rejection и
> безопасности (принцип 1). **Что C НЕ убирает:** физический лаг (теплоёмкость) — его снимает только упреждение
> по прогнозу возмущений (MPC), это Фаза 3 (энергооптимизация по прогнозу).

Стадийная воронка (каждая стадия отсекает кандидатов перед следующей, дорогой):

- **Stage 0 — feature store (есть).** Срезы контекста + переходы уже копятся: `device_events` (дельты с
  `triggerSource`/`mode`/`context`, P0-5), `sensor_readings` (1B), `auto_history` (1A/1F). Доп. сборка не нужна;
  при необходимости — материализация «контекст на момент перехода» как вью над event-log. **Для типа C —
  отдельная выборка установившихся окон:** пары `(возмущения → u_ss)` берутся только когда ошибка контура ≈ 0,
  интеграл стабилен, актуатор не дёргается (транзиенты в обучение feedforward НЕ идут — иначе выучим динамику,
  а не рабочую точку).
- **Stage 1 — дешёвый скрининг (отсев ~95% пар).** Взаимная информация `I(X;Y)` (ловит нелинейность —
  люкс→свет это порог, а не линия) и **условная** `I(X;Y|Z)` по конфаундерам (время суток, присутствие) +
  Granger-причинность (помогает ли прошлое сигнала X предсказать переход Y сверх собственной истории Y —
  уважает направление времени). **Поправка на множественные сравнения** (FDR/Benjamini-Hochberg) — без неё
  тысячи пар дают ложные «значимые». Всё реализуемо на чистом .NET (MI = гистограммы, Granger = линейные
  регрессии); тяжёлый пайплайн не нужен.
- **Stage 2 — генерация гипотез** (по типу кандидата). **A (правила):** **mining ассоциативных/последовательных
  правил** (FP-growth / PrefixSpan на C#) → человекочитаемые `IF контекст THEN действие` с support/confidence
  (форма «темно И движение → свет на 5 мин», лаги для «движение → свет в пределах T»); параллельно — **мелкое
  дерево/FastTree в ML.NET с Permutation Feature Importance** на выборке переходов (мультивходовые взаимодействия,
  ранжирование входов, конвертируется в правило). **B (уставка):** регрессия предпочтения → конфиг ML-setpoint
  блока (2B). **C (feedforward):** регрессия `g: возмущения → u_ss` на установившейся выборке Stage 0 → конфиг
  feedforward-аугментации существующего контура 1H (не имитация контроллера).
- **Stage 3 — валидация (анти-мусор).** Out-of-time бэктест кандидата **реплеем 1F** (`POST /api/replay` уже
  прогоняет правило по истории без команд) → метрика lift над base rate; проверка направления (Granger);
  **анти-петля** (исключить входы, которые двигает сам выход); фильтр self-fulfilling (не учиться на данных
  с `triggerSource=rule/ml` как на свободном выборе человека); пороги support/confidence/новизны. **Для типа C —
  closed-loop identifiability:** на данных замкнутого контура регрессия смещена (контур гасит тот самый сигнал) →
  обязательно сэмплировать только установившиеся окна (Stage 0), истинными входами брать **экзогенные возмущения**
  (улица/солнце/присутствие/цель), а не управляемую переменную; кандидат C валидируется как «снижает settling
  time / перерегулирование без выхода за клампы пола».
- **Stage 4 — предложение в очередь 2C.** Кандидат оформляется как `Proposed` правило (A), конфиг ML-блока (B)
  **или** feedforward-аугментация контура (C) с обоснованием (support/confidence/lift, backtest-scorecard,
  провенанс «замечено k раз»). Дальше — апрув и стадийный выкат через 2C/1F/2B; ни один кандидат не активируется
  без человека.

> **Размещение (по образцу остальных эпиков).** Тяжёлый майнинг — **периодический .NET-job**
> (BackgroundService, по образцу `MlTrainingService` из 2A) в Automation& Control Service (читает историю по
> HTTP из DbGateway, не лезет в Mongo напрямую) + ручной запуск `POST /api/discovery/scan`. Кандидаты
> персистятся как коллекция `proposals` в **DbGateway** (источник истины, по паттерну `automations`/`ml_models`)
> и поднимаются в UI очередью 2C. Новый сервис не вводим — переиспускаем рантайм/шину/загрузчики 1A/2A.

> **Риски (держать в контуре валидации Stage 3):** конфаундеры (всегда кондиционировать на время/присутствие),
> обратная причинность/петля (исключать собственные входы выхода), self-fulfilling и **ML-on-ML** (метить и
> исключать авто-/ML-данные — учитель только стационарный), **closed-loop identifiability для типа C**
> (сэмплировать установившиеся окна, входы = экзогенные возмущения), множественные сравнения (FDR), cold-start
> (rule-mining с порогом support отрабатывает раньше тяжёлых моделей; до накопления истории движок просто молчит),
> навязчивость (высокий порог уверенности; предложение, не действие).

- **DoD:** на накопленной истории движок **сам** находит ≥1 нетривиальную закономерность (тип A/B/C),
  формулирует её как `Proposed`-правило, конфиг ML-блока **или** feedforward-аугментацию контура с метриками
  (support/confidence/lift + backtest реплеем 1F; для C — снижение settling time / перерегулирования) и кладёт в
  очередь 2C; тривиальные, обратно-причинные и статистически случайные кандидаты отсеиваются Stage 1/3; ни одно
  предложение не активируется без апрува; скан запускается по расписанию и вручную.

> **✅ Реализовано (Epic 2F — v1, тип A/дискретные политики).** Полная воронка поверх заглушки 2C
> (`RuleSuggester`), в `AutomationService/Services/Discovery/`. **Статистические примитивы (чистый .NET,
> детерминированные):** [`InformationTheory`](../../src/Services/Domovoy.AutomationService/Services/Discovery/InformationTheory.cs)
> (MI и **условная** MI в натах — ловит нелинейность и снимает конфаундер), [`ChiSquared`](../../src/Services/Domovoy.AutomationService/Services/Discovery/ChiSquared.cs)
> (G-тест `G=2·N·MI` → p-value через регуляризованную неполную гамму = χ²-survival; Ланцош/цепная дробь),
> [`Statistics`](../../src/Services/Domovoy.AutomationService/Services/Discovery/Statistics.cs) (Benjamini-Hochberg
> FDR). **Ядро воронки** [`PatternMiner.Mine`](../../src/Services/Domovoy.AutomationService/Services/Discovery/PatternMiner.cs)
> (чистая функция, Stage 0→3): **Stage 0** — бакетизация истории в слоты, сенсор сэмплируется *как-of начало
> слота*, человеческие on-действия — *внутри* слота (сенсор всегда **предшествует** действию → уважает стрелу
> времени, снимает reverse-causality by construction); **Stage 1** — по каждой паре сенсор×актуатор условная
> `I(сенсор; действие | время-суток)` → G-тест p-value → **BH-FDR** по всем парам (здесь дохнут конфаундеры и
> случайные пары); **Stage 2** — для выжившей пары ищется предсказывающее условие: булев сенсор → «active»,
> числовой → **порог по терцилям** (`illuminance < t1` = «темно»→свет), опц. страж по времени суток; **Stage 3**
> — гейты support/confidence/**lift** (÷ base rate), учитель только `triggerSource=user` (без ML-on-ML/self-
> fulfilling), устройство не привязывается к себе. **Сервис-обёртка** [`DiscoveryEngine`](../../src/Services/Domovoy.AutomationService/Services/Discovery/DiscoveryEngine.cs)
> (BackgroundService по образцу `MlTrainingService`/`RuleSuggester`): читает event-log по HTTP из DbGateway,
> дедупит против правил+очереди, кладёт `Proposed`-`AutomationRule` (числовой `lt`/`gt`-триггер + опц.
> `TimeOfDay`-условие — реплеятся 1F) + `Proposal` (`Source=discovery`, богатый rationale support/confidence/
> lift/MI/p). `POST /api/discovery/scan` + прокси `ProposalsController.Discover`; WebUI `/proposals` — кнопка
> **«Искать закономерности»** рядом с «Найти предложения». **Инвариант (принцип 1):** движок — только поставщик
> гипотез, апрув и стадийный выкат те же (2C/1F/2B). **Тесты:** 16 офлайн-юнит — примитивы (MI=ln2/независимость/
> снятие конфаундера, χ²-survival vs критические значения, BH-FDR) + `PatternMiner` на синтетике (находит
> presence→light булевым и dark→light числовым порогом; отсекает self-wiring, non-user, несвязанное). Полное
> решение собирается 0 ошибок; WebUI tsc/lint/build + 29 тестов зелёные. **Решение по объёму:** v1 — **тип A**
> (дискретные политики → правила 1A) на MI/FDR-скрининге. Валидация реплеем 1F доступна ревьюеру кнопкой
> «Simulate» на предложении (как в 2C).
> **✅ Хвост (2026-07-06):** **Granger-скрининг** [`GrangerCausality`](../../src/Services/Domovoy.AutomationService/Services/Discovery/GrangerCausality.cs)
> — дискретный LR G-тест на счётчиках (вложенные мультиномиальные модели: `P(action_t|action_{t-1})` vs
> `+sensor_{t-1}` → χ²), опциональный гейт в Stage 1 (`DiscoveryGrangerAlpha`, **off by default** — строже
> as-of MI, требует плотной consecutive-slot истории; 3 юнит-теста). **Тип B** — майнер уставок-предпочтений
> [`SetpointPreferenceMiner`](../../src/Services/Domovoy.AutomationService/Services/Discovery/SetpointPreferenceMiner.cs):
> находит стабильные user-заданные числовые уставки (`temperature_setpoint`) по 6ч-бакетам (support+stddev,
> anti-loop=только user) → DiscoveryEngine эмитит **time-triggered** предложение «в HH:00 установить cap=V»
> (переиспользует Rule-proposal + replay-валидацию); 4 юнит-теста (131 .NET-юнит). **Тип C** (feedforward
> `возмущения→u_ss` для контуров 1H) — остаётся Фазе 3: нужны closed-loop данные контура + inverse-plant.

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
> зелёные.
> **✅ Хвост — каналы доставки (2026-07-06):** провайдер-агностичная доставка в AutomationService
> (`Services/Notifications`): `INotificationChannel` + `TelegramChannel` (Bot API) + `WebhookChannel`
> (generic JSON POST — покрывает ntfy/Gotify/Discord/Slack/push) + `NotificationDispatcher` (fan-out,
> изоляция сбоев каналов). **Env-gated, off by default** — без каналов уведомление только логируется
> (offline-first инвариант сохранён). Notify-действия правил (1A) идут через диспетчер (Shadow не шлёт).
> Endpoints `GET /api/notifications/channels` + `POST /api/notifications/test` + прокси `NotificationsController`;
> WebUI: карточка каналов + «Отправить тест» на `/logs`. **Дальше:** аномалии из 2B → уведомления.

### UI-направление: «дух дома» в интерфейсе 🚧 (v1 — 2026-07-04)

> **Идея (одобрено владельцем 2026-07-04):** передать идеологию имени (см. «Идеология имени» в
> [`positioning_ru.md`](positioning_ru.md)) через сам интерфейс — но **через поведение, а не декор**.
> Домовой невидим: личность живёт в голосе системы, ощущении присутствия и редких моментах.
> Анти-принципы (зафиксировано): никаких постоянных маскотов, скевоморфизма (текстуры/орнаменты),
> звуков, фольклорных названий в навигации, чат-аватара до реального LLM (2H/Фаза 3).

Принятые 7 пунктов и их статус:
1. **Голос от первого лица** ✅ частично — /proposals («Everything I would like to change…», «I went through
   the event log…»), empty-states, «новоселье». ⬜ Остаток: единый первочеловеческий пересказ ленты
   /api/activity (нужен слой маппинга `ActivityEntry`→фраза) + кнопка **«почему?»** на каждом действии
   ленты поверх атрибуции 1F.
2. **Стадии ML-авторитета языком доверия** ✅ — селектор и чип стадии на /blocks: Shadow — «watches and
   learns, never acts», Bounded — «acts within a careful band», Full — «trusted up to the safety floor».
3. **«Очаг» — индикатор присутствия** ✅ — тлеющий уголёк у логотипа (`HearthIndicator`): «дышит» при
   полном здоровье (по `/api/metrics/services`), ровный янтарный при деградации, серый без метрик;
   клик → /status; уважает `prefers-reduced-motion`.
4. **Дневник домового** ✅ частично — строка под заголовком дашборда (`DomovoyDigest`): ритм дома по
   режиму 1G + «I've made N adjustments today» (auto_history) + «M suggestions waiting for you» (2C).
   ⬜ Остаток: метрика тишины «N дней без ручного вмешательства» (нужно определение ручного
   вмешательства поверх event-log).
5. **Empty-states/404 с характером** ✅ — devices/automations/blocks/proposals + страница 404
   («I haven't been in this room yet»). Правило: личность только в редких состояниях.
6. **Приветствие по ритму дома** ✅ — часть DomovoyDigest («The house is asleep», «Watching over the
   empty house»…), режимы из 1G.
7. **«Новоселье»** ✅ — уведомление «A new resident has moved in: …» при появлении нового устройства
   (diff списка на клиенте; серверный push `DeviceDiscovered` в хабе объявлен, но не транслируется —
   ⬜ довести relay в ApiGateway, тогда мгновенно).

### UI-направление: мультиязычность (i18n) 🚧 (v1 — 2026-07-04, 2 языка)

> **Задача (владелец 2026-07-04):** переключение языков UI; сейчас 2 языка — **русский (по умолчанию)**
> и английский. Ключевое требование: **языки подгружаются без пересборки**. Редактор/загрузка новых
> языков — позже; сейчас готовим инфраструктуру.

**Решение:** `i18next` + `react-i18next` + `i18next-http-backend` + `i18next-browser-languagedetector`.
Переводы **не бандлятся** — грузятся в рантайме как статические JSON `/locales/{lng}/{ns}.json`
(`loadPath` у http-backend). nginx уже раздаёт статику (`try_files`). В `docker-compose.yml` смонтирован
volume `./src/UI/WebUI/public/locales:/usr/share/nginx/html/locales:ro` → **правка перевода = замена
JSON без `npm run build`**. Русская плюрализация (one/few/many) — из коробки CLDR-правилами i18next.

**Структура:** `src/i18n/` (`languages.ts` — data-driven реестр языков; `config.ts` — namespaces +
общие опции, типизирован `InitOptions`; `index.ts` — рантайм-инициализация с http-backend + детектором,
кэш языка в localStorage `domovoy-lang`; `format.ts` — `fmtDateTime/fmtDate/fmtTime`, читают активный
язык из синглтона i18next). Namespaces: `common` + `nav` (преднагружаются) и по одному на страницу
(`devices/zones/modes/automations/flow/blocks/models/proposals/plugins/logs/status/zigbee`) —
лениво. Переключатель — `components/i18n/LanguagePicker.tsx` рядом с ThemePicker/ColorModeToggle.
`App.tsx` обёрнут в `<Suspense>` (namespaces грузятся асинхронно). Тесты: отдельный инстанс i18n с
инлайн-ресурсами (glob по `public/locales`), язык фиксирован на `ru`, `useSuspense:false`.

**Готово в v1:** каркас + переключатель + все ~28 файлов страниц/компонентов переведены (ru — первичный,
en — параллельный); MUI-компонентов со встроенной локалью в приложении нет, поэтому `ruRU`-локаль MUI не
подключалась (отмечено на будущее). **Отложено (готовим инфраструктуру, не делаем сейчас):** редактор
переводов в UI, загрузка/добавление НОВОГО языка без пересборки (сейчас список языков `languages.ts`
бандлится → новый язык в пикере требует пересборки; правка контента существующих — уже без пересборки),
динамический реестр языков с бэкенда, MUI/`ruRU` + `date-fns`-локали при появлении таких компонентов.

### Эпик 2H. LLM — коннекторы-заглушки ✅
- Точка расширения под будущий LLM (NL-авторинг → `Proposed`-правило через 2C; объяснения «почему» прозой поверх 1F), **без** реальной модели (нет железа — разработка на ноутбуке).
- **DoD:** интерфейс/коннектор + фича-флаг есть; реальная интеграция — позже.

> **✅ Реализовано (Epic 2H — заглушка).** Провайдер-агностичная точка расширения в
> `AutomationService/Services/Assistant/`: интерфейс [`IAssistantConnector`](../../src/Services/Domovoy.AutomationService/Services/Assistant/IAssistantConnector.cs)
> с двумя способностями **вне контура управления** — `AuthorRuleAsync` (естественный язык → `Proposed`-правило
> через очередь 2C + валидация реплеем 1F) и `ExplainAsync` (атрибуция 1F «почему» → фраза). Поставляемая
> реализация — no-op [`DisabledAssistantConnector`](../../src/Services/Domovoy.AutomationService/Services/Assistant/DisabledAssistantConnector.cs):
> каждый запрос деградирует мягко (`Available=false` + сообщение), а не падает. **Фича-флаг**
> [`AssistantOptions`](../../src/Services/Domovoy.AutomationService/Configuration/AssistantOptions.cs) (`Assistant:Enabled`
> off по умолчанию + `Provider`); `IsAvailable` = флаг **и** назван провайдер (встроенных нет → инертна).
> Эндпоинты `GET /api/assistant/status`, `POST /api/assistant/author-rule|explain` + прокси `AssistantController`.
> WebUI: **без чат-аватара** (анти-принцип «духа дома») — только read-only карточка статуса на `/status`
> («Не настроен — точка расширения…»), i18n ru/en. Реальный бэкенд = будущая конфиг-выбираемая реализация того
> же интерфейса либо внепроцессный плагин (1C) — вызовы уже идут через интерфейс, дописать позже без правок
> здесь. **Тесты:** 6 офлайн-юнит (гейтинг флага, мягкая деградация author/explain, реклама способностей).
> Полное решение собирается 0 ошибок; WebUI tsc/lint/build + 29 тестов зелёные. **Наименования в коде** —
> функциональные (`assistant`/natural-language), провайдер-агностичные.

### Эпик 2J. Адаптер ESPHome (ESP32/ESP8266) через MQTT ✅ (v1)

> **Мотивация (обсуждено с владельцем 2026-07-04).** У платформы появляется **второй массовый DIY-путь**
> рядом с Zigbee2MQTT — платы ESP32/ESP8266. Для них два способа подключения, оба покрывают потребность:
> **(1) ESPHome через MQTT** — готовая экосистема (yaml-конфиги, OTA, сотни компонентов) без своей прошивки;
> **(2) Domovoy Native** — прошить плату нашей `DomovoyClient` (ESP32 Arduino-совместим, см.
> `docs/architecture/roadmap.md` capability-миграция §4 и memory `domovoy-native-protocol`) и попасть в уже
> готовый `DomovoyNativeAdapter` **без изменений на сервере**. Этот эпик реализует путь (1): адаптер
> укладывается в существующий [`IProtocolAdapter`](../../src/Services/Domovoy.Connectivity/Adapters/IProtocolAdapter.cs)
> (Эпики [1C](#эпик-1c-integration-sdk--внепроцессные-плагины-поверх-шины)/[1D](#эпик-1d-capability-адаптеры-под-целевые-домены))
> по образцу [`Zigbee2MqttAdapter`](../../src/Services/Domovoy.Connectivity/Adapters/Zigbee2MqttAdapter.cs) —
> **ядро, контракт, WebUI и ML не трогаются** (устройство поднимается по общему capability-пути автоматически).

**Почему MQTT, а не нативный ESPHome API.** Нативный API (protobuf/TCP :6053) не ложится на MQTT-only
`Connectivity` и потребовал бы своего TCP-клиента. При этом для нашего сценария (сенсоры + актуаторы +
ML-уставки + правила) он **почти ничего не добавляет**: HA MQTT Discovery несёт ту же модель сущности
(`device_class`/единицы/`min`/`max`/`step`/`options`/режимы света), а латентность/availability по MQTT
эквивалентны. API-only остаются лишь «ESP-как-периферия-хаба» фичи — **Bluetooth-proxy**, стриминг
голоса/камеры, кастомные сервисы, — не относящиеся к базовой телеметрии/управлению. Нативный API →
**возможное будущее расширение как внепроцессный плагин (1C)**, вводится точечно под конкретную потребность
(BLE-proxy/камеры), а не ради метаданных, уже доступных в Discovery.

- **Новый адаптер** `EspHomeMqttAdapter : IProtocolAdapter` в `Domovoy.Connectivity/Adapters/` (по образцу
  `Zigbee2MqttAdapter`): регистрируется в DI, `AdapterManager` подхватывает автоматически; вся логика
  publish/subscribe — внутри адаптера (как у Z2M), хост не маршрутизирует шину.
- **Discovery через HA MQTT Discovery.** ESPHome с компонентом `mqtt:` публикует на каждую сущность
  retained-конфиг `homeassistant/<component>/[<node>/]<object_id>/config` (component =
  sensor/binary_sensor/switch/light/number/select/climate/lock/cover/…). Адаптер подписан на
  `homeassistant/#`, парсит конфиг и через новый **codec `EspHomeCodec`** строит `DeviceDescriptor` +
  capabilities → публикует `DeviceDiscoveredV1` на канонической топологии. `deviceId =
  DeviceIdFactory.Derive("EspHome", <mac | device.identifiers | object_id>)` (стабильный hw-id из блока
  `device` конфига; несколько сущностей одной платы → **одно устройство**, как хаб в Native).
  `adapterSource = "EspHome"`.
- **Codec — таблица маппинга** (`component` + `device_class`/`unit` → capability `kind`), по аналогии с
  `Zigbee2MqttCodec.BuildModel`:

  | ESPHome component | Признак (`device_class`/`unit`) | Capability (kind) | Направление |
  |---|---|---|---|
  | `sensor` | temperature/humidity/carbon_dioxide/illuminance/power/… | `temperature`/`humidity`/`co2`/… (numeric) | read |
  | `binary_sensor` | motion/occupancy/door/window/… | `presence`/`occupancy`/`contact` (boolean) | read |
  | `switch` | — | `on_off` (boolean) | read/write |
  | `light` | brightness/color_temp/rgb | `on_off` + `brightness` + `color_temp` | read/write |
  | `number` | `min`/`max`/`step`/`mode` | writable numeric (напр. `*_setpoint`) | read/write |
  | `select` | `options[]` | enum (`Capability.Values`) | read/write |
  | `climate` | modes/target_temp | `temperature_setpoint` (+режим) | read/write |
  | `lock` | — | `lock` | read/write |
  | `cover` | position | `cover`/`position` | read/write |

- **State decode.** Из конфига берётся `state_topic` каждой сущности; адаптер подписывается и на каждое
  сообщение публикует `DeviceStateReportV1` (нормализованные значения). ON/OFF, числа, JSON-состояние света —
  декодируются codec'ом в capability-значения.
- **Command encode.** `Envelope<DeviceCommandV1>` (по паттерну Z2M — своя очередь `connectivity-EspHome-commands-v1`,
  игнор чужих `deviceId`) → `command_topic` из конфига; codec кодирует `Set` в ESPHome-payload (свет —
  `{"state":"ON","brightness":128}` или plain `ON`; `number`/`select` — значение/опция).
- **Availability (LWT).** ESPHome шлёт birth/last-will на `<topic_prefix>/status` (`online`/`offline`).
  Адаптер подписывается и мапит статус платы → `DeviceOnlineChangedV1` для всех её сущностей (как Native по
  `hub/<id>/status`), чтобы упавшая плата не «висела онлайн».
- **Развилка по топикам состояния.** `state_topic`/`command_topic` живут под произвольным `topic_prefix` (по
  умолчанию — имя платы), поэтому статически «забрать» их в `CanHandleTopic` нельзя. Рекомендуемое v1-решение —
  **конвенция `topic_prefix: domovoy/esphome/<node>`** в yaml → адаптер статически клеймит `homeassistant/#` +
  `domovoy/esphome/#`. (Альтернатива — динамически `SubscribeAsync` на выученные из discovery `state_topic` +
  вести set известных топиков для `CanHandleTopic`; сложнее, оставляем на потом.)
- **Известный риск (retained + wildcard).** RabbitMQ MQTT **не доставляет retained-сообщения на
  wildcard-подписки** (та же засада, что в Native discovery — см. memory `domovoy-native-protocol`): после
  рестарта `Connectivity` retained-конфиги ESPHome на `homeassistant/#` могут не прийти. Митигация: ESPHome
  переопубликовывает discovery на своём реконнекте + birth-message; при необходимости — периодический
  форс-реконнект/`discover`-широковещание по образцу `NativeProtocol.DiscoverTopic`. Держать в чек-листе
  живого прогона.
- **Конфигурация платы (docs).** Раздел с yaml-примером: `mqtt:` → адрес RabbitMQ MQTT-плагина (тот же брокер,
  что и весь стек), логин/пароль, `topic_prefix` по конвенции, `discovery: true`; включённые сущности
  (sensor/switch/light/number). OTA/секреты — по гайдам ESPHome.
- **Тесты.** Юнит на `EspHomeCodec` (discovery-config → capabilities для каждого component; command → payload;
  state → capability-values), по образцу тестов Z2M-codec. Интеграционный (Testcontainers, реальные
  RabbitMQ+Mongo): публикация HA-discovery-конфига + state на брокер → `DeviceDiscoveredV1`/`DeviceStateReportV1`
  → `EventInterceptor` → `capability_devices`/`device_events` (по образцу интеграционного теста 2D/P0-4).
- **WebUI — без изменений** (устройство приходит по общему capability-пути; архетип назначит `DeviceClassifier`
  из 2D по набору capability + `adapterSource=EspHome`). Опционально — иконка/лейбл источника «ESPHome».

- **DoD:** ESP32/ESP8266 с ESPHome+MQTT автоматически обнаруживается (сенсоры и актуаторы → capabilities),
  показывается на `/devices` с корректным архетипом (2D), отдаёт live-состояние и **исполняет команды**
  (свет/реле/`number`-уставка) сквозным путём команда→MQTT→устройство→подтверждение; падение платы (LWT)
  переводит её сущности в offline; codec и сквозной путь покрыты юнит- и интеграционным тестом. Путь Domovoy
  Native на ESP32 (прошивка `DomovoyClient`) работает без изменений сервера.

> **✅ Реализовано (Epic 2J — v1).** Новый [`EspHomeMqttAdapter : IProtocolAdapter`](../../src/Services/Domovoy.Connectivity/Adapters/EspHomeMqttAdapter.cs)
> в `Domovoy.Connectivity` (регистрируется в DI, `AdapterManager` подхватывает автоматически; вся логика
> publish/subscribe — внутри адаптера, как у Z2M). **Codec** [`EspHomeCodec`](../../src/Services/Domovoy.Connectivity/Adapters/EspHomeCodec.cs)
> парсит HA-MQTT-Discovery-конфиг (`homeassistant/<component>/[<node>/]<object_id>/config`) в набор
> **каналов** (capability + свои state/command-топики + decode/encode): `sensor`→numeric (temp/hum/co2/lux/
> power/energy/battery по `device_class`), `binary_sensor`→`occupancy`/`contact`, `switch`/`light`→`on_off`
> (+`brightness` со шкалой 0..`brightness_scale`), `number`→writable numeric (temp→`temperature_setpoint`),
> `select`→enum по `options`, `lock`→`lock`; неизвестные величины → кастомный capability id из `object_id`
> (открытая модель); ключи читаются с HA-аббревиатурами (`stat_t`/`cmd_t`/`dev_cla`/…). Несколько сущностей
> одной платы (общий `device.identifiers`) → **одно устройство** (`deviceId = DeviceIdFactory.Derive("EspHome",
> <identifier>)`), как хаб в Native. **State:** топики выучиваются из discovery и подписываются динамически
> (+статически claim `homeassistant/#` и конвенция `domovoy/esphome/#`) → `DeviceStateReportV1`. **Command:**
> `Envelope<DeviceCommandV1>` (своя очередь `connectivity-EspHome-commands-v1`, игнор чужих) → encode на
> `command_topic` канала. **Availability (LWT):** топик платы (`payload_available`/`not_available`, дефолт
> `online`/`offline`) фанится в `DeviceOnlineChangedV1` по всем сущностям. `nan`/`inf` от недоступного сенсора
> отбрасываются. **Ядро/контракт/WebUI/ML не тронуты** — устройство идёт по общему capability-пути (архетип
> назначит 2D по `adapterSource=EspHome`). **Тесты:** 13 юнит на codec (маппинг каждого компонента, decode/
> encode, группировка платы, availability, аббревиатуры) — 82/82 .NET-юнит зелёные. **Известный риск**
> (retained на wildcard в RabbitMQ MQTT — см. Native) держим в чек-листе живого прогона: митигация — ESPHome
> переопубликовывает discovery на реконнекте + birth.
> **✅ Хвост (2026-07-06):** добавлены многотопиковые компоненты **`cover`** (open/close→`on_off` + `position`
> 0..100), **`climate`** (`temperature_setpoint` + read `temperature` + enum `hvac_mode` по `modes`), **`fan`**
> (`on_off` + `fan_speed` 0..100 по percentage-топикам) и **`light` со `schema:json`** (один JSON-payload на
> топик → `on_off`+`brightness` из общего топика). Адаптер: один state-топик теперь раздаётся **нескольким
> каналам** (JSON-light) → объединённый `DeviceStateReportV1`. Новые capability-id `position`/`hvac_mode`/
> `fan_speed`. 4 новых codec-теста (17 codec-юнит). **Дальше:** presets/oscillation/stop/tilt, нативный
> ESPHome API как внепроцессный плагин (BLE-proxy/камеры).

### Эпик 2K. Геолокация участка + настройки (runtime-editable) ✅ (2026-07-06, ветка `epic-2k-location-settings`)

> Прикладной эпик после закрытия основных эпиков Фазы 2: местоположение установки должно задаваться из UI
> (а не только из `appsettings`), потому что от него зависят sun-триггеры (1A/1D `sun_gate`), локальное время
> и будущие фичи (Commute 2M). Принцип 2 (offline-first) — жёсткий инвариант эпика.

- Местоположение (lat/lon/метка) и таймзона — **редактируются в рантайме**, без передеплоя.
- Страница настроек в WebUI с картой для выбора точки.
- **DoD:** локация меняется из UI и сразу применяется к sun-расчётам без перезапуска; смена работает офлайн (ручной ввод координат), онлайн-геокодер опционален.

> **✅ Реализовано (Epic 2K).** **Ключевое решение (одобрено владельцем):** sunrise/sunset **уже** считается
> локально офлайн ([`SunCalculator`](../../src/Services/Domovoy.AutomationService/Services/SunCalculator.cs) —
> астрономическая формула по lat/lon), поэтому **никакого внешнего sunrise-API/таблиц не добавляем**.
> Геокодер (Nominatim) используется **только и опционально** — превратить название/клик по карте в координаты
> при редактировании; ручной ввод lat/lon всегда работает офлайн. Таймзона выводится из координат офлайн через
> **GeoTimeZone** (NuGet, встроенные tz-шейпы) с ручным override.
> **Backend:** контракт [`SiteLocation`](../../src/Common/Domovoy.Contracts/Home/SiteLocation.cs) (singleton POCO,
> Id="current"). DbGateway [`SettingsEndpoints`](../../src/Gateway/Domovoy.DbGateway/Endpoints/SettingsEndpoints.cs)
> (`/api/settings`): GET/PUT `location` (upsert в `site_location`, PUT выводит tz через GeoTimeZone если не
> закреплена), GET `timezone`, GET `geocode`/`reverse-geocode`; `IGeocoder`/`NominatimGeocoder`
> ([Services/Geocoder.cs](../../src/Gateway/Domovoy.DbGateway/Services/Geocoder.cs)) — typed HttpClient,
> все ошибки глушатся в пустой результат (offline-first). ApiGateway
> [`SettingsController`](../../src/Gateway/Domovoy.ApiGateway/Controllers/SettingsController.cs) прокси.
> **AutomationService:** `SunCalculator` теперь **runtime-mutable** (lat/lon за `volatile`-ссылкой на immutable
> record; `RefreshLoop.RefreshLocation` тянет `GetLocationAsync` каждый цикл и перенацеливает калькулятор при
> изменении; `appsettings` остаётся seed/fallback). **WebUI:** маршрут `/settings` + пункт-шестерёнка в навигации;
> [`pages/Settings.tsx`](../../src/UI/WebUI/src/pages/Settings.tsx) — карточка Location (поиск места → геокодер;
> **Leaflet-карта** клик/drag для выбора; ручной lat/lon; метка; авто-tz + override; сохранение) + карточка
> Appearance; [`components/settings/LocationMap.tsx`](../../src/UI/WebUI/src/components/settings/LocationMap.tsx)
> (react-leaflet@4 + фикс путей marker-icon под бандлер). 4 новых .NET-юнит-теста (repoint SunCalculator +
> геокодер offline/parse), WebUI `tsc` + 32 vitest + `vite build` зелёные. **Не проверено вживую** против
> Mongo/Nominatim.

### Эпик 2L. Платформенные виртуальные сенсоры (Sun / Time / Calendar) ✅ (2026-07-06, ветка `epic-2k-location-settings`)

> Данные платформы (солнце, время, календарь) — как **first-class виртуальные сенсоры**: устройства без
> «железа», которые видны в дашборде и работают в правилах как любой сенсор. Прямое продолжение слоистой модели
> и той же идеи, что control-blocks 1H проецируются в устройства.

- Sun / Time / Calendar как обычные устройства (дашборд + правила + история).
- **DoD:** солнечные/временные/календарные величины видны как сенсоры, участвуют в условиях правил по
  `deviceId`+`capabilityId`, пишут телеметрию; праздники задаются вручную + опциональный онлайн-импорт.

> **✅ Реализовано (Epic 2L — Sun + Time + Calendar).** **Ключевой инсайт архитектуры:** существующий пайплайн
> уже превращает *любого* издателя на шине в first-class устройство — источник, публикующий `DeviceDiscoveredV1`
> + `DeviceStateReportV1` (как адаптер/блок), попадает в `capability_devices` (дашборд), `DeviceRegistry`
> (движок правил через `DeviceState`-триггеры/условия) и историю. Т.е. виртуальный сенсор = «адаптер без
> железа». Выбран **отдельный [`SystemSensorService`](../../src/Services/Domovoy.AutomationService/Services/SystemSensorService.cs)**
> (а не control-block — блок требует ручного инстанса; системные сенсоры должны существовать всегда):
> `AdapterSource="System"`, детерминированные id `DeviceIdFactory.Derive("System","<kind>")`, анонс раз +
> republish состояния раз в минуту. **Sun:** capabilities `sun_elevation`/`sun_azimuth`/`is_dark`/`is_day`/
> `sunrise`/`sunset`; `SunCalculator.Position(instant)` = **алгоритм NOAA** (офлайн). **Time:** `time_of_day`
> (локальные минуты от полуночи) + `clock` (HH:mm). **Calendar:** `day_of_week`/`is_weekend`/`is_holiday`/`date`.
> Локальное время — через новый синглтон [`SiteContext`](../../src/Services/Domovoy.AutomationService/Services/SiteContext.cs)
> (tz из `SiteLocation`, 2K); календарь — через [`CalendarContext`](../../src/Services/Domovoy.AutomationService/Services/CalendarContext.cs)
> (выходные + даты праздников), оба обновляются `RefreshLoop`. Контракт
> [`CalendarSettings`](../../src/Common/Domovoy.Contracts/Home/CalendarSettings.cs) (singleton: `WeekendDays` +
> `Holidays`); `SettingsEndpoints` GET/PUT `/api/settings/calendar` + POST `…/import?country=&year=` — импорт
> праздников через **Nager.Date** ([`HolidayImporter.cs`](../../src/Gateway/Domovoy.DbGateway/Services/HolidayImporter.cs),
> офлайн→пусто). **Защита объёма истории (важно):** `EventInterceptor` для `AdapterSource=="System"` пишет
> числовые дельты **только в телеметрию** (`sensor_readings` — красивые тренды) и **не засоряет** `device_events`
> поминутным шумом; логируются только дискретные переходы (стало темно, сменился день недели). **Вокабуляр:**
> новые `CapabilityIds` + `WellKnownCapabilities`-хелперы (+ `Text()`), архетипы `Sun`/`Clock`/`Calendar` +
> правило `DeviceClassifier` (`System` → архетип из `system/<kind>`). **WebUI:** иконки/лейблы/headline
> `deviceVisuals` для sun/time/calendar; карточка **Calendar** в Settings (выходные + чипы праздников +
> онлайн-импорт по стране/году). **19** .NET-юнит-тестов (NOAA + ComputeSun/Time/Calendar), WebUI `tsc` + **41**
> vitest + `vite build` зелёные. **Не проверено вживую** против шины/Mongo/Nager.
> **Примечания:** устройства анонсятся `ZoneId=Empty` → «Unassigned» (можно назначить зону); старые спец.
> `TriggerType.Sun`/`ConditionType.Sun` оставлены для обратной совместимости, но теперь это частный случай
> общих `DeviceState`-условий над сенсором Sun.

### Эпик 2M. Commute-плагин: время в пути и подготовка к приезду 🟡 (2M.1 ✅ 2026-07-06; первый живой out-of-process плагин)

> **✅ 2M.1 реализовано и проверено вживую (2026-07-06, ветка `epic-2k-location-settings`).** Плагин
> `src/Plugins/Domovoy.CommutePlugin` (.NET-console-app на `Domovoy.MessageBus`+`Domovoy.Contracts`),
> манифест-папка `plugins/commute-planner/` (`internet: true`). **Первый реально запустившийся внепроцессный
> плагин**: `PluginSupervisor` обнаружил → прогейтил по `internet` → запустил процессом → плагин подключился к
> шине, анонсировал устройство «Commute» и принял команды. Проверено `docker compose`: `GET /api/plugins`
> показал `commute-planner` = **Running** (а `example-anpr-camera` = `Blocked` по GPU — контраст gating);
> команды `going`/`deadline` через `POST /api/device-control/{id}/set` → пересчёт → состояние осело в
> read-model (напр. deadline к 19:00 → вечерний час-пик `heavy` → `leave_by` 17:39). Traffic-коннектор
> **заменяемый** (`ITrafficProvider`): по умолчанию офлайн-`SimulatedTrafficProvider` (детерминированный ETA
> из дистанции × суточная кривая пробок — плагин гоняется без облачного ключа), `TomTomTrafficProvider`
> включается при `COMMUTE__PROVIDER=tomtom`+`COMMUTE__TOMTOMAPIKEY`. Origin — `SiteLocation` (2K) по REST,
> destination — `lat,lon` или геокодер 2K. Адаптивный цикл: реже в простое, чаще к моменту выезда. Env для
> дочернего процесса объявлены на сервисе `plugin-supervisor` (супервизор их наследует). 28 юнит-тестов
> (Simulated-провайдер + гео-математика + дедлайн/интервал). **Осталось:** 2M.2 (notification-порт+stub),
> 2M.3 (S3, future).

> **Двойная ценность.** Это первая **прикладная** фича-плагин — и одновременно **первый реально
> запускающийся внепроцессный плагин**: сейчас в репозитории есть лишь пример `example-anpr-camera`, и тот в
> статусе `Blocked` (требует GPU), т.е. система плагинов ([Эпик 1C](#эпик-1c-integration-sdk--внепроцессные-плагины-поверх-шины))
> **Уточнение по мосту наружу (2026-07-08).** Владелец решил **не строить доставку через ботов чужих платформ**
> (Telegram и т.п.) — вместо этого основной путь уведомлений и взаимодействия — **собственные приложения**
> (в т.ч. Android в режиме киоска как настенные пульты-мониторы). Поэтому 2M.2 переопределён в **первопартийный
> LAN-канал на SignalR** (см. дорожки ниже), а само приложение-панель выделено в отдельный **Эпик 2O**.
>
> ни разу не прогонялась вживую. Commute прогоняет **весь контракт 1C от начала до конца**: манифест +
> resource-gating (**первое реальное использование гейта `internet: true`**), out-of-process-запуск +
> autorestart, discovery, публикация состояния и **приём команд** (двусторонность). По принципам 2/3: внешний
> traffic-API — опциональная облачная зависимость, изолированная в отдельном процессе (падение/таймаут API не
> задевает ядро; при отсутствии интернета плагин просто `Blocked`).

**Сценарии.**
- **S1 «еду в …».** Origin = дом, dest указывает пользователь → разовый расчёт ETA с пробками → сообщить.
- **S2 «надо быть в … ко времени T».** Origin = дом → рекомендуемое **время выезда** (API-параметр `arriveAt`
  отдаёт leave-by с прогнозом пробок — не программируем вручную) → **обновлять с адаптивным интервалом** в окне
  до выезда, алертить при сдвиге.
- **S3 «возвращаюсь домой из …» (🔬 future).** Инверсия: origin = *живое положение хозяина* (снаружи дома),
  dest = дом → оптимистичный ETA → **дом готовится к приезду** (отопление/вентиляция под время прибытия).

**Дизайн (ложится на существующую архитектуру).**
- **Одно виртуальное устройство «Commute»** через capability-модель (как control-blocks 1H проецируются в
  устройства): writable-входы `commute:mode` (`going`/`deadline`/`returning`), `commute:destination`,
  `commute:deadline`, `commute:origin`; readable-выходы `commute:eta_minutes`, `commute:traffic_level`,
  `commute:leave_by`, `commute:arrival_eta`, `commute:minutes_to_arrival`. Задать цель = записать в
  writable-capability (штатный actuation-путь 1D → `DeviceCommandV1` плагину); результат — обычный сенсор
  (дашборд/правила/история/зоны **бесплатно**). Задавать можно из UI, правила или ассистента (2H author-rule).
- **Origin (дом)** плагин тянет по публичному REST `GET /api/settings` (`SiteLocation`, Эпик 2K) — ядро как
  чёрный ящик, **runtime-editable локация подтягивается автоматически**, без перезапуска плагина.
  **Destination** — геокодер 2K. Конфиг плагина (API-ключ) — через env/`config.json`/`args` (граница конфига
  плагина по 1C).
- **Traffic-провайдер — бесплатный ярус:** TomTom (~2500 req/день, live traffic + `arriveAt`) либо HERE.
  **Оговорка по РФ:** качество пробок Yandex выше, но его routing-API платный → коннектор делаем **заменяемым**
  (тот же порт, другой ключ). Для дома лимитов free-tier хватает многократно (адаптивный опрос одной поездки —
  единицы-десятки запросов/час).
- **Реализация плагина** — маленький **.NET-console-app** на `Domovoy.Contracts` + `Domovoy.MessageBus` (заодно
  проверяем реиспользуемость контрактной сборки внешним процессом; polyglot/Python тоже возможен — контракт это
  JSON на шине).

**Мост «дом ↔ внешний мир» — абстрагируем за порт со stub (реализация НЕ зависит от готовности моста).**
> Способ организации моста владельцем **пока не решён** — поэтому строим по образцу provider-agnostic заглушки
> Эпика 2H: **порт-абстракция + disabled-stub (+ возможна симуляция в эмуляторе)**, а не конкретный канал.
> Ключевое наблюдение: **уведомления (наружу — долг 2G, где каналы доставки отложены) и приём геопозиции
> (внутрь — для S3) суть две половины одного моста.** S1/S2 функционально полны и без реального моста —
> результат виден как сенсор в UI/истории; «push» уходит в порт, у которого пока stub-реализация (лог/эмулятор).

**Дорожки.**
- **2M.1 — Commute-плагин (S1/S2 расчёт). ✅ 2026-07-06.** Манифест + console-app + traffic-коннектор + вирт.
  устройство «Commute» + адаптивный цикл опроса. Закрывает расчёт S1/S2; прогнал 1C end-to-end вживую.
- **2M.2 — Первопартийный LAN-канал уведомлений (SignalR, без облака).** Порт-абстракция уже построена
  (`NotificationDispatcher` + `INotificationChannel`, реализации Telegram/Webhook как stub); **решение владельца
  (2026-07-08): не развивать каналы через чужих ботов, основной путь — собственное приложение.** Работа 2M.2:
  новый `SignalRNotificationChannel` (в `AutomationService`) публикует `NotificationRaisedV1` на шину → в
  ApiGateway `NotificationRelayService` (по образцу `EventRelayService`) ретранслирует на `DeviceHub` →
  React-клиент (открыт / киоск на стене) показывает баннер. Полностью локально, переживает выключенный интернет,
  off-by-default. **Граница:** гарантия доставки, пока клиент в LAN; доставка вне дома (фон/push через ОС) —
  осознанно вынесена в Эпик 2O. Telegram/Webhook остаются опциональными, но не как основной путь. Гасит долг 2G.
- **2M.3 — S3 (🔬 future).** Инверсия действия: `commute:minutes_to_arrival` → штатные правила/блоки прогрева
  (консистентно со слоистой моделью — плагин даёт сенсор, детерминированный слой действует; связка с тепловой
  моделью ML-термостата 2B). Гейтится на **приём геопозиции** (OwnTracks/MQTT или Telegram live-location) —
  «внутренняя» половина моста — и модель лида прогрева.

**DoD:** плагин запускается **отдельным процессом**, гейтится по `internet`, переживает краш API (autorestart);
публикует устройство «Commute»; **S1** считает ETA от `SiteLocation` (2K) до цели через бесплатный API; **S2**
выдаёт `leave_by` (`arriveAt`) и обновляет его адаптивным циклом; результаты видны в `/devices` и истории;
уведомление уходит через **порт-заглушку** (лог/эмулятор) → фича функционально полна **без реального моста**;
S3 помечен future. Проверяет систему плагинов (1C) на реальном сквозном прогоне.

### Эпик 2N. «Дневник дома»: художественная летопись жизни дома 🟡 (ядро Фазы 0–3 реализовано 2026-07-09, ветка `develop`)

> **Статус реализации (2026-07-09).** Детерминированное ядро (Фазы 0–3) готово, русский язык, офлайн, без
> LLM. Новая библиотека `Domovoy.Narrative` (язык-нейтральный IR `Beat`/`Scene`/`DayStory` в
> `Domovoy.Contracts.Narrative`; шов `INarrativeRenderer` + селектор; детерминированный `RuLanguagePackRenderer`,
> управляемый **данными** — встроенный JSON-пакет `Packs/ru.json`; новый язык = новый пакет, без кода).
> Конвейер: `DiaryMiner` (device_events→биты, обогащение архетипом 2D/зоной P0-3/персоной) → `SceneBuilder`
> (коалесцинг по причинному корню + причина = предшествующий System-сенсор 2L, язык-нейтральный `CauseBeat`) →
> `SignificanceScorer` (редкость/Vacation/люди/аномалии/причинность/широта) → `DayConsolidator`
> (топ-сцены, «молчание — фича») → рендер → материализация `home_story` фоновым `HouseDiaryBuilder`
> (детерминированная ротация синонимов `narrative_state`, tier-затухание старых незначимых дней). API
> `GET /api/home-story` + `POST /preview|/rebuild`; персонализация `narrative_entities` (имя духа/жильцов);
> прокси `HomeStoryController`; WebUI — тумблер «Дневник» на `/logs` + диалог имён. Решение А-vs-B: движок
> **in-process (B)** как база, плагин/LLM (A) — опциональный слой поверх шва (Фаза 4, future). `home_story` —
> **обычная** коллекция (не time-series: нужен идемпотентный upsert для перестроя). Тесты: 22 офлайн .NET
> (рендерер: род/число/падеж, анафора, cooldown-детерминизм; SceneBuilder; significance; BSON round-trip
> `HomeStoryEntry`) + 3 vitest; полное решение + WebUI собираются. **Не прогонялось вживую** (Фаза 1.5).
> Фаза 4 (LLM/плагин-стилист через 2H) — future, не реализована.

> **Идея (owner).** Опциональный **дневник** в разделе «Журналы», где события описываются не техническим
> логом, а художественно: «Домочадцы зажгли свет в гостиной», «Стало темнеть — и дух дома засветил веранду»,
> «Похолодало, и домовой поддал жару в котле». Ближайшие ~7 дней — подробно; глубже остаётся только **значимое**
> → складывается связное повествование о жизни дома, а при определённых пользователях (Эпик 2E) — и о действиях
> жильцов. Название проекта (**Домовой**) работает на концепт: автоматика = «дух дома»/«домовой», люди =
> «домочадцы».

> **Ключевой инсайт архитектуры — сырьё уже есть.** [`ActivityEndpoints`](../../src/Gateway/Domovoy.DbGateway/Endpoints/ActivityEndpoints.cs)
> (Эпик 2G) уже сводит три источника в единый `ActivityEntry`, а под ним `DeviceEventLog` несёт **все три
> компонента художественной фразы**: **кто** (`TriggerSource` + `RuleId`; при 2E — конкретный пользователь),
> **что** (`CapabilityId` + `OldValue→NewValue` + архетип/зона устройства, 2D), **почему** (для rule/block —
> условие сработки из атрибуции 1F; для «стало темнеть/похолодало» — виртуальные сенсоры 2L как first-class
> устройства). Плюс режим дома штампуется на событии (1G). Т.е. «почему» — обычно самый дефицитный слот NLG — у
> нас есть бесплатно. Генерация — классический **template NLG (slot-filling), без LLM в горячем пути**.

**Ключевые решения дизайна.**
- **Дневник, а не журнал (owner).** Единица повествования — **день**, не событие: 2–4 значимых «бита» за сутки
  консолидируются в короткий абзац с датой. **Молчание — фича:** ниже significance-порога за день не пишется
  ничего («сегодня дом дремал»). Это снимает и монотонность, и «повествование на каждый чих».
- **Actor выводится из `TriggerSource`** (persona-слой = `switch`): `manual`/user → «домочадцы» (или имя из 2E);
  `rule`/`block` → «дух дома»/«домовой»; `ml_*`-губернаторы (2B) → домовой с оттенком суждения; System-сенсоры
  (2L, sun/time/calendar) → безличное «стало темнеть», «наступил вечер».
- **Агрегация событий в «сцены».** Переход в Night дёргает N устройств → одна фраза, а не N строк: коалесцинг
  по причинному корню `(RuleId | mode-change, time-bucket)`. Детерминированно, без ML.
- **Причинная связка.** Для rule/block-события поднимаем условие триггера (1F) и превращаем в придаточное «стало
  X → домовой сделал Y». У ручных действий причины нет — фраза просто короче.
- **Significance-функция (главный фильтр дневника).** Скор на сцену: редкость (первое похолодание сезона),
  смены режима (Vacation всегда значимо), присутствие/действия людей (2E обычно значимее автоматики), аномалии
  (активность в 3 ночи, устройство ушло offline — есть liveness-watchdog). **Затухание истории** = наша
  существующая retention-механика (1B TTL + `$dateTrunc`-роллапы): < 7 дней держим всё, глубже TTL выкашивает
  низкозначимое → остаётся «летопись».

**NLG-движок (реализация фразы — главная инженерная работа).**
- **Сущность = грамматически-размеченный пул синонимов (owner-добавление).** Синоним — не строка, а запись
  `{ text, gender, number, animacy, prep, loc_form }`: выбор синонима **автоматически задаёт форму глагола и
  местоимения**. Narrator почти весь м.р. (домовой/дух/хозяин/старый дом) — согласуется легко; болит на
  «жильцах» (домочадцы pl ↔ семья f.sg — разное число) и «местах» (в/на + предложный падеж).
- **Морфологию не генерируем — храним готовые формы.** У зоны — предлог + предложный падеж («на веранде», «в
  гостиной») как поле; у пары архетип×capability — таблица вариантов глагола под 3 рода/число актёра. Библиотеки
  склонения — оверкилл, не тащим.
- **«Живость» = анафора, а не пестрота.** Ввести → сократить: первое упоминание за запись — полное имя
  (ротируемый синоним), дальше — местоимение/короткая форма (род берётся из метаданных синонима). Против
  «тезаурусной болезни»: **cooldown/LRU** выбор синонима (не рандом — `Math.random`/`Date.now` в скриптах
  недоступны, храним last-used-индекс в состоянии истории) + **якорь рассказчика на запись**.
- **Персонализация.** Пользователь может **сам назвать** духа своего дома и жильцов (связка с реальными именами
  2E) — маленькая самостоятельная фишка.

**LLM — строго опционально, поверх детерминированного каркаса (degrade offline).**
> Дневник **обязан** работать на шаблонах как база (иначе ломается без сети и стоит денег на каждый чих). Точка
> расширения 2H (`IAssistantConnector`, сейчас stub) даёт красивый гибрид **по нашей же governance-дисциплине
> `propose → approve → deterministic execution`** (как очередь 2C): (1) LLM генерирует **пул синонимов один раз
> при заведении сущности** → человек утверждает → рантайм чист; (2) LLM-стилист собирает **недельный дайджест**
> из готовых «битов». LLM — **не источник фактов** (никаких галлюцинаций в «памяти дома»), а стилист поверх
> гарантированно корректного каркаса; при выключенном 2H всё работает на дефолтных пулах.

**Модель данных.**
- `home_story` (Mongo) — материализованные записи дневника (дата, абзац-текст, ссылки на исходные сцены,
  significance-скор, tier). Строится **фоновым билдером** из тех же трёх источников (по аналогии с
  `EventInterceptor`), а не на лету — значимость считаем один раз и храним; TTL/tier по 1B-механике.
- `narrative_entities` — пулы синонимов с грамматической разметкой для narrator / residents (2E) / places
  (зоны) / устройств (архетипы 2D). Дефолтные пулы в комплекте, пользовательские правки поверх.

**Фазы (черновик).**
- **0 — Контракт + on-the-fly рендер.** `home_story`/`narrative_entities` контракты, дефолтные пулы, движок
  фразы (slot→форма→анафора) над одиночным событием, endpoint-предпросмотр. Без агрегации и значимости.
- **1 — Агрегация в сцены** (коалесцинг по причинному корню) + причинная связка через 1F.
- **2 — Significance-функция + дневная консолидация + билдер-сервис** (материализация `home_story`) + tier/TTL
  затухание (переиспользуем 1B-retention).
- **3 — UI-режим** «История/Дневник» на `/logs` (тумблер поверх Activity 2G) + редактор пулов синонимов и
  имён (персонализация, связка 2E).
- **4 (🔬 future) — LLM-слой** через 2H: генерация пулов под аппрув (очередь 2C) + недельный дайджест-стилист.

**DoD:** на `/logs` есть опциональный режим «Дневник»; за день с значимыми событиями формируется связный
художественный абзац с корректным русским согласованием (род/число/падеж) и живой ротацией имён/местоимений; дни
без значимых событий тактично пропускаются; ≤ 7 дней подробно, глубже — только значимое; всё детерминированно и
**работает офлайн без LLM**; персонализация имён духа/жильцов доступна из UI. LLM-слой (Фаза 4) помечен future и
не является условием готовности.

> **Зависимости/порядок.** Опирается на 2G (Activity — источник), 1F (атрибуция «почему»), 2L (System-сенсоры
> для «стало темнеть/похолодало»), 2E (жильцы), 1B (retention-затухание) — все ✅. Т.е. 2N можно делать в любой
> момент после них; ядро (Фазы 0–3) — чисто на шаблонах, LLM-слой ждёт вызревания 2H/Фазы 3.

> **Порядок (реком.):** Фаза 1.5 → **2D + 2G** рано (фундамент UX/данных, низкая зависимость) + **2A**
> параллельно (пайплайн ML) → **2B → 2C** дозревают по мере накопления истории (ров «обучающаяся
> автоматизация») → **2F** следом за 2C (нужны накопленная история + очередь как приёмник предложений) →
> **2E** (мелкий, в любой момент) → **2H** в конце. 2F можно начинать со Stage 1 (скрининг) как только
> история достаточна, не дожидаясь 2B. **2J (ESPHome-адаптер) — независим от ML-ветки**, можно делать в любой
> момент параллельно (расширяет фронт устройств и данных для ML → полезно делать раньше, вместе с 2D).

### Эпик 2O. Панели: приложение-пульт/монитор пространства 🔵 (концепт, план зафиксирован 2026-07-08)

> **Решение владельца (2026-07-08).** Взаимодействие и уведомления идут через **собственные приложения на
> разных платформах**, а НЕ через ботов чужих платформ (Telegram и т.п.). Отдельный класс использования —
> **режим киоска для Android**: то же приложение переиспользуется как **настенные пульты-мониторы**,
> размещаемые в разных пространствах дома для быстрого доступа/контроля. Ориентир — мобильное приложение HA,
> но с явным акцентом на панель-в-пространстве. Уведомления здесь — лишь одна из функций, поэтому это отдельный
> эпик, а не часть 2M.

> **Ключевой рычаг — почти всё уже есть, новый только тонкий слой платформы и идентичности панели.**
> - **Контент** — кастомные дашборды (custom-dashboards, Mongo `dashboards`): панель показывает назначенную
>   вкладку/дашборд как «домашний экран».
> - **Live-состояние + уведомления** — `DeviceHub`/`EventRelayService` (ApiGateway) + новый канал уведомлений
>   из **2M.2** (`NotificationRaisedV1` → relay → `DeviceHub` → баннер на клиенте).
> - **Идентичность/доступ** — роли (2E) + зоны: панель = субъект с ролью `kiosk`, привязанный к зоне.

**Что реально новое (тело эпика).**
1. **Оболочка платформы.** PWA как общий знаменатель существующего WebUI; TWA/нативная обёртка под Android
   (в т.ч. киоск). iOS/десктоп — позже. Ядро UI не форкаем — оборачиваем.
2. **Регистрация панели.** Экземпляр-панель регистрируется как устройство/субъект: `id` + зона + назначенный
   дашборд + роль `kiosk`; управление из `/settings` (связка 2K-настроек и 2E-ролей).
3. **Режим киоска.** Блокировка навигации вне назначенного дашборда, авто-возврат на «домашний» экран по
   бездействию, dim/wake по присутствию/времени (presence 1G + Sun/Time 2L).
4. **Приём уведомлений.** Потребитель события `Notification` из 2M.2: на стене — баннер; в фоне/убитом
   приложении — задел под доставку через ОС (см. ниже).

**Доставка вне LAN (push) — осознанно отложена.** 2M.2 гарантирует доставку, пока клиент в LAN (SignalR, без
облака). Доставка при закрытом/фоновом приложении вне дома требует push через ОС (FCM/APNs — облако Google/Apple,
либо self-hosted UnifiedPush/ntfy). Это вводит внешнюю зависимость и хранилище токенов устройств → делаем
**отдельной дорожкой позже** и **опционально** (off-by-default, инвариант принципа 2 сохраняется: базовая
гарантия — локальный SignalR).

**Дорожки (черновик).**
- **2O.1 — PWA-базлайн + приём уведомлений.** Манифест PWA, установка на Android/десктоп, потребление канала
  `Notification` из 2M.2 (баннер). Не требует нативной сборки.
- **2O.2 — Регистрация панели + режим киоска.** Модель панели (id/зона/дашборд/роль `kiosk`), UI в `/settings`,
  блокировка навигации + авто-возврат + dim/wake.
- **2O.3 — Нативная Android-обёртка (киоск).** TWA/обёртка для настенного размещения (lock task mode,
  автозапуск, always-on).
- **2O.4 (🔬 future) — Push вне LAN.** Опциональный канал доставки через ОС (UnifiedPush/ntfy предпочтительнее
  FCM ради offline-first), реестр токенов устройств.

**DoD (v1 = 2O.1+2O.2):** существующий WebUI устанавливается как PWA; панель регистрируется с привязкой к зоне и
дашборду и роли `kiosk`; в режиме киоска показывает назначенный дашборд, не даёт уйти с него и возвращается на
домашний экран по бездействию; уведомления из 2M.2 приходят баннером, пока панель в LAN; всё работает при
выключенном интернете. Нативная Android-обёртка (2O.3) и push вне LAN (2O.4) — отдельные дорожки, не условие v1.

> **Зависимости.** Опирается на 2M.2 (канал уведомлений), custom-dashboards (контент), 2E (роль `kiosk`), зоны,
> 2K-настройки (регистрация/управление панелями), 1G+2L (dim/wake). 2M.2 — прямой предшественник; остальное ✅.

---

# Перегруппировка forward-фаз по итогам рыночного исследования (2026-07-19)

> **Что произошло (owner-решение 2026-07-19).** Проведено [рыночное исследование 2025–2026](../../memory-bank/marketResearch2026.md)
> (deep-research: HA/Homey/Hubitat/openHAB/Apple/Google/Alexa/SmartThings/Aqara/NN-g/энергетика/безопасность/
> Matter/голос). Ключевой вывод: **рвы Домового уже построены** (обучающаяся автоматизация 2A–2F, объяснимость 1F,
> control-blocks 1H, offline-first) — но продукту не хватает **базовой домовладельческой зрелости и доверия**,
> которая есть у всех конкурентов: бэкапы/миграция (пробел №1 — «хаб умер → дом умер»), сцены, энергодомен,
> per-person presence, зрелый движок правил (wait/on-error/переменные), дисциплина уведомлений. Это **не
> «интеллект» (Фаза 2 закрыта) и не «автономность»** — это отдельный пласт «сделать продуктом».
>
> **Договорённость по правкам карты (owner, 2026-07-19):** закрытые фазы (0/1/1.5/2) **не трогаем** — только
> отмечаем хвосты; forward-часть **перегруппирована и переномерована** под новый контекст. Старая «Фаза 3 —
> Автономный дом» стала **Фазой 5** без изменения содержания (только сдвиг номера + калибровки из исследования).
> **Ссылки в закрытых эпиках Фазы 2 на «Фазу 3»** (auth/замки/видео/голос/enforcement, напр. 2E/2N/2H) теперь
> читаются как **Фаза 5** — контент перенесён 1:1, сдвинулся только номер.
>
> **Раскладка по приоритетам исследования (секция Б):** **Фаза 3 = Приоритет-1** (фундамент готов, высокая
> ценность / низкая-средняя цена); **Фаза 4 = Приоритет-2** (высокая ценность / средняя цена, домен-расширения);
> **Фаза 5 = Приоритет-3 + автономность** (стратегическое, калибровка). Опорные принципы (слоистость, offline-first,
> resource-aware, contract-first, fail-safe, «сначала данные») действуют без изменений; **новые фичи закрывают
> «боли конкурентов» из секции В** (подписочная усталость, «хаб умер», смерть Amazon Hunches, notification fatigue,
> сложность HA, RU-голос) как явные преимущества.

---

# Фаза 3 — Продуктовая зрелость и доверие (ближайшее, фундамент готов)

**Цель фазы:** превратить «умный, но не готовый к дому» сервер в **продукт, которому доверяют ежедневно и который
переживает катастрофу**. Каждый эпик закрывает фичу-must-have, которая есть у всех конкурентов, но которой у
Домового нет, — при уже готовом фундаменте (Приоритет-1 исследования). Ров остаётся прежним; здесь мы убираем
причины, по которым продукт нельзя порекомендовать обычному домовладельцу.

### Эпик 3A. Бэкапы / восстановление / миграция — как продукт 🚧 (пробел №1; v1 реализован 2026-07-19)

> **Статус v1 (2026-07-19, НЕ прогонялось вживую в docker-стеке).** Реализовано: полный бандл
> (zip: `manifest.json` + `collections/*.bson` всех пользовательских коллекций через Mongo-драйвер,
> без бинарника mongodump; time-series пересоздаются по опциям из манифеста; `ops_logs` исключён) +
> настройки плагинов (`.settings`, том в db-gateway) + опц. `extras`-каталоги (restore — в staging).
> Расписание (ежедневно, таймзона площадки 2K) + ретенция keep-N (`backup_settings`, `BackupScheduler`);
> UI на `/settings` («Бэкапы»: расписание, «создать сейчас», список, download/upload/restore/delete);
> restore = drop+recreate по манифесту → возврат настроек плагинов → рестарт стека по шине
> (`SystemControlV1 all/restart`). API `/api/backup/*` (прокси в ApiGateway, download/upload стримятся).
> Тесты: 4 интеграционных (real Mongo, round-trip incl. time-series, ретенция, валидация upload,
> path-traversal) + 3 vitest секции. Доки: [`docs/backup_restore_ru.md`](../backup_restore_ru.md).
> **Остаток до ✅:** живой прогон в docker-стеке (restore на чистом стеке + миграция хоста по DoD),
> off-site слой (осознанно отложен).

> **Пробел №1 исследования** (grep: ноль упоминаний в репо и карте). «Хаб умер — дом умер» — **страх №1
> self-hosted**; HA сделал это темой **двух релизов подряд** (2025.1 автобэкапы+ретенция, 2025.2 расширяемые
> хранилища), Hubitat **берёт за это деньги** (Hub Protect — бэкап включая радио-сети + миграция без переспаривания,
> доказанная willingness-to-pay). Это же — принцип 5 (fail-safe) на уровне продукта: критичное состояние восстанавливается.

- **Полный bundle:** `mongodump` всех коллекций (`capability_devices`, TS `device_events`/`sensor_readings`, `zones`,
  `automations`, `control_blocks`, `block_state`, `ml_models`+артефакты, `proposals`, `home_state`, `dashboards`,
  `users`/`roles`, `site_location`, `calendar`, `home_story`/`narrative_entities`) + конфиги сервисов + **settings
  плагинов** (`.settings/{id}.json` из 1C). Состояние Zigbee-сети (координатор/pairings Z2M) — **отдельный трек**
  (радио нельзя dump'нуть из Mongo): документируем бэкап `coordinator_backup.json` Z2M + путь миграции стика.
- **Расписание + ретенция + UI на `/settings`** (по образцу 2K/2G): периодические бэкапы, число хранимых копий,
  ручной «Backup now»/«Restore»/«Download». Восстановление = стоп-стека → restore dump → старт. **Миграция на новый
  хост** = тот же bundle. Опциональный **off-site** (шифрованный, образец Nabu Casa — платный слой без lock-in) —
  но **локальный бэкап первичен** (offline-first; off-site off-by-default).
- **DoD:** из UI создаётся полный бэкап (данные + конфиги + настройки плагинов) по расписанию с ретенцией; restore на
  чистом стеке возвращает дом в рабочее состояние; миграция на новый хост проверена; Zigbee-координатор бэкапится
  документированным путём; всё локально, off-site опционально. Закрывает «боль конкурентов» №2.

### Эпик 3B. Сцены как first-class объект 🚧 (v1 реализован 2026-07-19, ветка `epic-3b-scenes`)

> **Статус v1 (2026-07-19, НЕ прогонялось вживую в docker-стеке).** Реализовано: коллекция `scenes` +
> контракт `Domovoy.Contracts.Scenes.Scene` (`targets[]` = device+capability-set, нормализация JsonElement
> как у правил); CRUD в DbGateway + прокси в ApiGateway; **активация** `POST /api/scenes/{id}/activate`
> (fan-out `DeviceCommandV1`, source `scene:{id}`, нормализация к примитивам как в `DeviceControlController`);
> действие **`scene`** в движке правил (append `ActionType.Scene` + `RuleAction.SceneId`, резолв через новый
> `SceneStore` в RefreshLoop, уважает Shadow); WebUI: страница `/scenes` (список, активация, **Re-Capture** =
> снимок writable-состояния выбранных устройств, редактирование значений **без активации**), **плитка-сцена**
> на кастомных дашбордах (тип `scene`), scene-действие в редакторе правил; i18n ru/en. Тесты: 2 интеграционных
> (real Mongo, round-trip + normalization) + vitest (страница сцен + scene-действие). tsc/eslint/vitest (172) и
> сборка .NET зелёные. Доки: [`docs/scenes_ru.md`](../scenes_ru.md). **Остаток до ✅:** живой прогон в стеке
> (включение из UI/правила/дашборда → команда → устройство), опц. переходы (fade) — отложены.

> Есть у всех (Homey **Moods**, HA scenes, Hubitat **Room Lighting**); NN/g подтверждает ценность пресетов
> (снижение когнитивной нагрузки). **Сцена ≠ автоматизация** — отдельная сущность. Проектные требования с 1-го дня
> из болей конкурентов: **редактирование БЕЗ активации** (боль WTH-2024, закрыта в HA 2024.12) + **Re-Capture**
> («снять текущее состояние в сцену», Hubitat).

- Сцена = именованный **снапшот целевых capability-состояний** набора устройств/зон. Активация = уже существующий
  actuation-путь (батч `DeviceCommandV1`, 1D) — новый action-тип `scene` в движке правил (1A) + плитка-сцена на
  дашборде (custom-dashboards) + вызов из ассистента (2H author). Хранение — коллекция `scenes` в DbGateway (паттерн
  `automations`). Capability-модель и команды уже готовы — сцена их только группирует.
- **Re-Capture:** снять текущее состояние выбранных устройств в сцену одной кнопкой. **Редактирование значений сцены
  без применения к дому** (обязательное требование). Опц. переходы (fade) там, где capability это поддерживает.
- **DoD:** сцена создаётся из текущего состояния (Re-Capture) или вручную, редактируется без активации, применяется
  одной командой из UI/правила/дашборда; `scene`-action доступен в движке правил и flow-редакторе (1E).

### Эпик 3C. Энергодомен: дашборд + тарифная сущность + блок «дешёвые часы» ⬜

> Самый «продаваемый» дашборд 2025–2026 (HA 2025.4 parent-child против двойного счёта, 2025.8 Sankey, 2025.11 pie;
> Homey Energy). Механика «включи в N самых дешёвых часов» в HA до сих пор собирается из blueprints — **ниша для
> first-class примитива**. Стыкуется с ML-уставками (EMHASS-lite **без Python**). Полная энергооптимизация (MPC,
> прогноз PV/батарея) — это уже **Фаза 5**; здесь — видимость + дешёвые часы.

- **(1) Учёт kWh** поверх готовой телеметрии + rollups (1B) и батч-агрегации (dashboard-fill): потребление по
  устройству/зоне, parent-child иерархия против двойного счёта. **Powercalc-механика (Приоритет-2, сюда же):**
  виртуальные power/energy-профили (LUT: лампа по яркости/цвету) → покрытие устройств **без розеток-измерителей**;
  профиль = виртуальный сенсор (образец `SystemSensorService` 2L).
- **(2) Тарифная сущность** = виртуальное устройство с capability `price`/`tariff` (статические тарифы из UI +
  опциональный онлайн-провайдер Nord Pool/Tibber как **плагин 1C**; офлайн → работает на статике). Контракт: kWh-сенсоры
  + тарифная сущность (как в HA).
- **(3) Блок «дешёвые часы»** — обобщённый control-block (2Q): вход = тариф-серия + длительность окна → выход `on_off`
  «сейчас дешёвый час» → двигает deferrable-нагрузки (бойлер/зарядка/полив) штатным actuation-путём. Plunge-pricing
  (отрицательные цены) как частный случай.
- **(4) Дашборд-тип «Энергия»:** потоки (Sankey-стиль), деньги по тарифу, Top Consumers — тип вкладки в custom-dashboards.
- **DoD:** дашборд показывает потребление по устройствам/зонам и деньги по тарифу; тариф задаётся из UI (статика или
  провайдер, офлайн-устойчиво); блок «дешёвые часы» включает нагрузку в N дешёвых часов; устройство без измерителя
  получает оценочное потребление через Powercalc-профиль.

### Эпик 3D. Presence как платформенный сигнал (per-person) ⬜

> У всех 5 экосистем «кто дома» — **базовый примитив** (Apple «первый пришёл/последний ушёл», Google/Alexa per-member).
> У Домового есть только блок `presence_mode` + writable Home-устройство (1G) — **но нет источника присутствия**
> (`PresenceMonitor` был удалён). Этот эпик добавляет **слой источников** под уже существующий контур режимов.

- **Житель = виртуальное устройство** с capability `presence` (связка с `users` 2E). Агрегат по всем жильцам
  («кто-то дома»/«все ушли», первый/последний) → кормит блок `presence_mode` (1G) и правила/сцены/охрану (4B).
- **Слои источников (по надёжности):** (1) **геофенсинг** — MQTT-endpoint, **совместимый с OwnTracks** (privacy-first,
  шлёт прямо в свой MQTT!) + связка с 2O PWA; (2) **Wi-Fi/BLE room-level** в LAN (ESPresense/Bermuda через ESPHome
  bluetooth-proxy — плагин 1C); (3) **mmWave-зоны** — Aqara FP2/FP300 (до 30 зон, 5 человек, детект по дыханию)
  **уже подключаемы по Zigbee2MQTT**, физически работают сейчас.
- **DoD:** житель = устройство с `presence`; ≥1 источник (геофенсинг OwnTracks-совместимый) обновляет его офлайн-локально;
  агрегат «кто-то дома»/«все ушли» доступен правилам и режимам 1G; per-person сигнал доступен правилам, сценам и (Фаза 4)
  охране. Заменяет удалённый `PresenceMonitor` **источником данных**, не логикой режимов.

### Эпик 3E. Движок правил: wait-for-event, required expression, глобальные переменные, on-error ⬜

> **Hubitat Rule Machine — золотой стандарт движков правил**; **отсутствие catch-ошибок** — подтверждённая **открытая
> боль HA** (WTH-2024); Homey — error-ветки из любой карточки. Наш движок 1A реактивный, без mid-action ожидания,
> переменных и обработки ошибок — это потолок выразительности, который упирают продвинутые пользователи.

- **(1) Wait-for-event с timeout** (mid-action: «жди открытия двери ≤5 мин, иначе ветка B») — расширение
  `ActionExecutor` (уже асинхронный, держит `delay`). **(2) Required expression** (Hubitat) — гейт срабатывания +
  **отмена pending-действий** при выходе условия из истины. **(3) Глобальные переменные** (Hub Variables, 5 типов) —
  на базе персистентной `block_state` (2Q) как именованные vars, читаемы/писаемы правилами и блоками, переживают
  рестарт; connector-устройства (переменная как виртуальное устройство). **(4) On-error ветки** — карточка ошибки как
  ветвь (Homey), текст ошибки как контекст; в flow-редакторе 1E.
- **Инвариант слоистости:** безопасный пол (1A) **не троттлится и не гейтится** пользовательскими выражениями/ошибками
  (принцип 1) — новые механики только над пользовательским слоем.
- **DoD:** правило умеет ждать событие с таймаутом и ветвиться по ошибке; required-expression гейтит и отменяет pending;
  глобальная переменная задаётся/читается правилами и блоками и переживает рестарт; всё выражается в flow-редакторе (1E).

### Эпик 3F. Дисциплина уведомлений (таксономия, per-type каналы, rate-limit, actionable) ⬜

> **NN/g 3-0:** таксономия reactive/proactive/optimization; выбор канала per-type пользователем; **safety-критичное —
> никогда только через низковидимые каналы**; notification fatigue → эффект **«cry wolf»** (пример: алерты влажности
> каждые 30 с → пользователь выключила всё). У конкурентов это наросло исторически — у Домового шанс **заложить с
> 1-го дня**. Прямо расширяет pending-треки **2M.2** (LAN SignalR) и **2O** (панели), не дублирует их.

- **(1) Таксономия** reactive/proactive/optimization в `NotificationRaisedV1` (payload плана 2M.2). **(2) Per-type
  выбор канала** пользователем; safety-класс — запрет доставки только тихим каналом. **(3) Rate-limiting/dedup** в
  `NotificationDispatcher` (хвост 2G уже есть) — против «cry wolf». **(4) Actionable notifications** — `actions[]` в
  payload → кнопка → команда в шину **с атрибуцией актора** (actor-string журнала уже есть, journal-attribution):
  «принять предложение 2C прямо из уведомления», UNLOCK-кнопка по связке комнаты (образец HomeKit).
- **DoD:** уведомление несёт тип и per-type канал; safety не уходит только тихим каналом; повторы дедупятся/троттлятся;
  actionable-кнопка исполняет команду с атрибуцией актора; «одобрить предложение 2C из уведомления» работает.
  Питает 2M.2/2O.

### Эпик 3G. UX редактора правил: target-picker с контекстом + человеко-читаемые триггеры ⬜

> HA 2025.11 (target-picker с полным контекстом: устройство + область + **сколько сущностей затронет**) + 2026.7
> («Automations that speak your language» — интент-триггеры «когда в спальне ниже 18°C», это **не LLM и не free-text**,
> а структурные блоки человеческой формулировкой). Наша **capability + архетип** модель это уже позволяет **без
> изменения backend** — WebUI-only поверх обобщённых `RuleEditors` (2026-07-18).

- **Target-picker с контекстом:** при выборе цели правила показывать устройство + зону + охват («затронет N устройств»).
  **Человеко-читаемые формулировки триггеров** поверх архетипов/capabilities: «когда в спальне ниже 18°C» =
  `zone(спальня) × temperature × lt(18)`. Единая точка добавления блоков, двухпанельный диалог (HA-планка).
- **DoD:** при выборе цели виден контекст (зона/охват); триггеры формулируются человеко-читаемо поверх архетипов;
  backend (`AutomationRule`) не меняется. Закрывает «боль конкурентов» №5 (сложность HA) на уровне авторинга.

### Эпик 3H. Абстракция хранилища: выбор БД (Mongo default + альтернативы) ⬜

> **Owner-решение 2026-07-19 (решение №8, дополнение к решению №2).** Mongo остаётся дефолтом, но продукт
> должен дать **выбор БД** (как HA recorder: MariaDB/PostgreSQL — «пользователи это любят»), плюс это
> страховка от платформенных сюрпризов Mongo (ARMv8.2 на Pi, AVX на старых x86 — вскрылось при анализе
> железа к плану развёртывания). Сейчас фиксируем **проектирование шва**; сам переход задачей не является.

- **Анализ сцепки (2026-07-19).** Граница уже есть на уровне системы: с Mongo говорит **только DbGateway**
  (остальные сервисы — HTTP/шина); исключение — Serilog Mongo-sink `ops_logs` из всех сервисов. Внутри
  DbGateway repository-слоя нет: `IMongoDatabase` в DI, эндпоинты/сервисы зовут `GetCollection`/`Builders`/
  агрегации напрямую (26 файлов, ~253 точки; самое сцепленное — `HistoryEndpoints`). Mongo-специфика:
  time-series коллекции + TTL через `collMod` (`TimeSeriesInitializer`), `$dateTrunc`-роллапы, capped
  `ops_logs`, кастомный `JsonElementSerializer`, inline ML-артефакты.
- **Ф0 (1–2 дня):** ADR + карта store-интерфейсов. Ключевое правило: интерфейсы = **доменные операции**
  (`ITelemetryStore.AggregateAsync(ids, window, bucket)`, `IDeviceStore`, `IActivityStore`, …), никаких
  IQueryable/FilterDefinition наружу — иначе шов фиктивный.
- **Ф1 (~1–1.5 нед):** вырезать шов внутри DbGateway + Mongo-реализации; эндпоинты тонкие; интеграционные
  тесты параметризованы по бэкенду. Ценна сама по себе (тестируемость + страховка) — делается до второй БД.
- **Ф2 (~2–3 нед):** коннектор **PostgreSQL** — лучший «реляционный» кандидат: JSONB ≅ гибкие документы,
  `date_trunc` ≅ `$dateTrunc` 1:1, артефакты → bytea; TTL/capped → app-level фоновые чистки (переносимо,
  без pg_cron); Testcontainers.PostgreSql. **НЕ TimescaleDB community** (TSL = source-available → ломает
  лицензионное правило); обычные таблицы + BRIN/партиции достаточно для домашнего масштаба. MariaDB — только
  по спросу пользователей (JSON слабее). Serilog-sink — выбор по конфигу в `SerilogBootstrap`.
- **Ф3 (опц., +1–2 нед каждый):** SQLite (микро-инсталляции), MariaDB.
- **Постоянная цена (осознанно):** каждая новая коллекция/запрос = N реализаций × N тестовых матриц →
  матрицу держим **max Mongo (default) + PostgreSQL**. Наша поверхность шире HA recorder (там сменна только
  история; у нас DbGateway = конфиг + история + ML-артефакты).
- **DoD (Ф1):** ни один эндпоинт/сервис DbGateway не использует MongoDB.Driver напрямую (только store-слой);
  интеграционные тесты зелёные через шов. **DoD (Ф2):** стек полностью работает на PostgreSQL переключением
  конфига; интеграционные тесты проходят на обоих бэкендах; матрица поддержки зафиксирована в доках.

---

# Фаза 4 — Домен-расширения (среднесрочно)

**Цель фазы:** расширить продукт **вширь** — новые домены (охрана, свет, энергия-аналитика, сеть, план дома) и каналы
поверх зрелого ядра Фазы 3 (Приоритет-2 исследования). Часть доменов требует полной авторизации (Фаза 5) для жёсткого
enforcement — здесь строим **модель/данные/UX**, критичное исполнение (PIN, замки) ждёт auth.

### Эпик 4A. Иерархия локаций (дом→этаж→комната) как адресуемая семантика ⬜

> **openHAB semantic model** (Location → Equipment → Point + Property): питает авто-дашборды по комнатам, правила-мишени
> («весь свет на кухне»), будущий голос (Фаза 5, «выключи свет на кухне» резолвится семантикой). У нас есть Zone-граф
> (P0-3, `ParentZoneId` + `Zone.Kind`) — но **адресация «все X в Y»** и семантические роли отсутствуют. Стыкуется с
> zone-scoping ML-шаблонов (2I).

- Расширить Zone семантическими ролями поверх готового графа; **адресация по (зона × архетип/capability)** → «все
  `light` в `kitchen`» для правил/сцен/дашбордов; авто-дашборд по комнате из семантики (HA Areas-дашборд 2025.4).
  Голос-резолюция — задел под Фазу 5.
- **DoD:** адресация «все <архетип> в <зоне>» доступна правилам/сценам/дашбордам; иерархия дом→этаж→комната отражена;
  авто-дашборд по комнате строится из семантики; готов hook под голосовую резолюцию.

### Эпик 4B. Охранный домен (Alarmo/HSM-модель) ⬜ (enforcement — Фаза 5)

> **Alarmo** (референс поверх HA) / Hubitat **HSM**: areas + зоны сенсоров, до 5 arm-режимов с индивидуальным
> периметром, exit/entry delay + trigger time, PIN-пользователи. **Страховой аргумент:** скидки 3–5% за датчики,
> **7–10% за авто-отсечку**. Режимы 1G + роли 2E + watchdog-культура уже есть; датчик+кран — Zigbee, работает через Z2M.

- **Arm-режимы** (до 5) поверх Home/Away/Night/Vacation с per-state периметром сенсоров; каналы **вторжение/протечка/дым**;
  entry/exit/trigger delays; тайл arm/disarm; per-mode таблица реакций. **Готовый архетип-паттерн «протечка → кран»**
  (Aqara Water Leak T1 ~$15 + Valve Controller T1 на шаровый кран, Zigbee) → реакция **<2 c локально**. **PIN-пользователи
  и enforcement** (кто может arm/disarm) — модель готовим, жёсткое исполнение = Фаза 5 (auth поверх ролей 2E).
- **DoD:** настраивается ≥2 arm-режима с периметром сенсоров; протечка → закрытие крана срабатывает <2 c локально;
  тайл arm/disarm; PIN/enforcement помечены под Фазу 5 (модель — готова).

### Эпик 4C. Циркадное освещение как свойство лампы (Adaptive Lighting) ⬜

> **Apple Adaptive Lighting:** циркадная температура — **свойство лампы, не правило**: включил любым способом —
> температура уже правильная. Ежедневно заметный эффект; солнечный виртуальный сенсор (NOAA, 2L) уже есть.

- **«Перехватчик включения»:** событие `off→on` лампы с `color_temp` → мгновенная установка по циркадной кривой (на
  базе Sun 2L / времени). Реализация — control-block (2Q) + перехват on-события, кривая настраивается. Работает офлайн.
- **DoD:** включённая любым способом лампа получает циркадную `color_temp` мгновенно; кривая настраивается; офлайн.

### Эпик 4D. ML энерго-аномалии (standby/«вампиры»/нетипичное → 2C) ⬜

> В self-hosted мире **не автоматизировано никем** (у HA/Homey/Hubitat — нет); академический мейнстрим (Isolation
> Forest, автоэнкодеры). `ml_tasks` (2P) + discovery (2F) + энергодомен (3C) превращаются в **видимую уникальную
> ценность**. Инвариант учителя тот же — не ML-on-ML.

- Поверх энергодомена 3C и воронки 2F — детектор аномалий потребления (standby-вампиры, нетипичные всплески/паттерны)
  на ML.NET → предложение в очередь **2C** с обоснованием; апрув и стадийный выкат — те же (принцип 1).
- **DoD:** детектор находит standby-вампир/аномалию на истории потребления и кладёт предложение в 2C с метриками;
  активация только через апрув очереди.

### Эпик 4E. Карта топологии сети в UI (Zigbee → позже Thread/Matter) ⬜

> Hubitat (встроенные карты Z-Wave/Zigbee + health + ремонт из UI) и HA Matter Server 9.0 задали планку. Продолжение
> нашей liveness-watchdog культуры (device-liveness).

- Топология Zigbee из Z2M (граф маршрутизации + LQI/health) → визуализация (reactflow, как block-graph 1E) + health-
  статусы + ремонт/переспаривание из UI. Позже — Thread/Matter (Фаза 5).
- **DoD:** экран показывает граф Zigbee-сети с health-статусами; проблемные/слабые узлы видны; заложен hook под
  Thread/Matter-топологию.

### Эпик 4F. Community-канал шаблонов (rule/composite templates + shareable URL/QR) ⬜

> **openHAB marketplace** («форум = магазин», без тяжёлой инфраструктуры; Rule Templates с мастером параметров) +
> **SmartThings Shareable Routines** (автоматизация → URL/QR, установка в один клик). Галерея AddBlockPicker (2Q) и
> zip-плагины (1C) уже есть — **не хватает внешнего канала обмена**.

- Параметризуемые rule/composite-шаблоны (2Q-шаблоны + DSL `param` уже есть) → экспорт/импорт как **URL/QR**
  (SmartThings-стиль) + мастер запроса параметров (openHAB) + опциональный форум/каталог. Галерея расширяется внешними
  шаблонами.
- **DoD:** шаблон правила/композита упаковывается в переносимый URL/QR и ставится в один клик с запросом параметров;
  внешний шаблон появляется в галерее.

### Эпик 4G. План дома как тип дашборда (пины устройств на поэтажном плане) ⬜

> **Alexa Map View** (LiDAR-скан → поэтажный план с пинами, управление тапом по месту). **Ручная загрузка плана
> покрывает 90% ценности без LiDAR** — тип вкладки в существующие custom-dashboards.

- Новый тип элемента/вкладки в custom-dashboards (Mongo `dashboards`): загруженное изображение плана + пины устройств
  (позиция) + этажи слоями; управление тапом по месту (штатный actuation-путь).
- **DoD:** пользователь грузит план, расставляет пины устройств, управляет тапом по месту; этажи переключаются слоями.

### Эпик 4H. Persistence-фильтры телеметрии + restoreOnStartup ⬜ (малый)

> **openHAB** per-item strategies (everyChange/threshold/time-filter/restoreOnStartup). Снижает шум телеметрии,
> повышает выживаемость состояния. Частично уже есть (`block_state` 2Q, TTL 1B) — обобщаем per-signal.

- Per-signal стратегия записи (everyChange / порог / time-filter) в `EventInterceptor`/телеметрии (1B);
  restoreOnStartup-семантика для виртуальных/writable состояний (расширение `block_state`).
- **DoD:** сигнал можно писать по порогу/времени, а не на каждый семпл; выбранное состояние восстанавливается на старте.

---

# Фаза 5 — Автономный дом (долгосрочно, концепт)

**Цель фазы:** дом управляется преимущественно сам, с речевым интерфейсом и видеоаналитикой, полной безопасностью
доступа и энергооптимизацией. Уровень — направления и открытые вопросы, без детализации задач (Приоритет-3
исследования + перенесённая автономность бывшей Фазы 3). Опорные принципы (offline-first, слоистость, resource-aware)
неизменны.

- 🔬 **Полная безопасность доступа + умные замки (перенесено из Фазы 2).** Локальная авторизация (логин/токены/сессии
  поверх ролей 2E), TLS/auth на MQTT и шине, аудит — и только затем **умные замки и контроль доступа** (домофон,
  открытие по событию). **Гостевой доступ** (time-boxed, только выбранные устройства, с журналом — образец Apple Guest
  Access) поверх ролей 2E + Activity-журнала (2G). Делаем позже: auth мешает разработке, замки нельзя без auth.
- 🔬 **Matter/Thread** как **additive/сходящийся** стандарт (наряду с основным Zigbee2MQTT). **Выбор matter.js
  подтверждён переездом HA** (Matter Server 9.0, июнь 2026, с Python+C++ SDK) — прямое подтверждение
  концепт-плана Epic-Matter (мост как not-MQTT-источник через matter.js-sidecar). Целиться в **1.5.1+**; capability-маппинг **от кластеров устройства, не от
  версии спеки** (экосистемы отстают на 1.5–2 версии); рекомендовать Домовой **единственным fabric** (multi-admin жрёт
  батарейки sleepy-устройств); Thread — **присоединяться по credential sharing (1.4)**, не строить свою сеть.
  **Отслеживать Thermostat Suggestions (1.6, июнь 2026) — стандартизация нашего governor-паттерна `ml_thermostat`**
  (потенциально «ml_thermostat говорит с любым Matter-термостатом стандартным каналом»).
- 🔬 **Локальный голосовой ассистент** как edge-плагин — **интегрировать готовый локальный стек (STT/intent/TTS), а НЕ
  строить с нуля** (HA закрыл тему: Voice PE, Piper/Ollama, streaming TTS). Слоёная архитектура: **microWakeWord
  (ESP32-S3) → phrase-constrained STT → LLM-fallback**. **Русский локальный стек — ничейная ниша** (Speech-to-Phrase RU
  нет, Piper RU средний) = **риск и дифференциатор** RU-first Домового. Локальный LLM для tool-calling — Qwen3 4B/8B на
  8 ГБ VRAM. Задел под **проактивный `start_conversation`** (дом заговаривает первым). На шину — интенты/команды/ответы;
  низкая задержка → строго локально.
- 🔬 **LLM-слой (дозревание 2H)** — доращивать extension point (сейчас заглушка) до: (а) **tool-calling контракта**
  (LLM → структурированная capability-команда), (б) **Ollama-бэкенда первосортным** (Qwen3-класс на опциональном узле
  8 ГБ VRAM, resource-aware 1C), (в) UX по планке Gemini/Alexa+ (NL-авторинг правил через 2C, «Suggest with AI» в
  формах, **AI-описания событий как апгрейд «Дневника дома» 2N** — наш офлайн-NLG фундамент, LLM опция). Философия
  **«одобряешь модель, не выводы»** подтверждена стратегией HA («AI — инструмент, не властелин») и **смертью Amazon
  Hunches** (09.06.2026 — непрозрачная облачная проактивность отвергнута) → staged authority + proposals Домового = верная ставка.
- 🔬 **Видеоаналитика / камеры.** Дешёвый путь — **Frigate-события по MQTT как «камера-датчик»** (0.16 добавил лица+автономера
  бесплатно; person/car/animal → триггер правил, почти бесплатно на нашем MQTT). Полный NVR/WebRTC — отдельный эпик
  уровня Matter-моста; на шину идут **только события**, не сырое видео. Aqara G5 Pro (локальный NPU) доказывает
  реализуемость локального видео-ИИ на дешёвом железе.
- 🔬 **ML в active-режиме** — модели управляют климатом/светом в безопасных границах, продолжая дообучаться; пользователь
  корректирует, система адаптируется. Безопасный пол (1A) неотключаем над ML. **Углубление ML:** counterfactual
  loop-replay (прогон контура с предложенными параметрами по истории) для строгой валидации непрерывных ML-выходов;
  **тип C движка 2F** (feedforward `возмущения→u_ss` для контуров 1H) требует closed-loop данных — сюда же.
- 🔬 **Энергооптимизация (надстройка над 3C).** Тарифы + прогноз PV + батарея → 24-ч план deferrable-нагрузок, **MPC-режим**
  (упреждение по прогнозу возмущений — снимает физический лаг теплоёмкости, чего не делает feedforward). Эталоны
  self-hosted: **EMHASS** (линейное программирование) и **Predbat** (планировщик батареи). Ориентир UX — Homey Energy.
  V2H/V2G — **наблюдать, не делать** (бренд-привязано и дорого).
- 🔬 **Облачные интеграции внешних устройств** (робот-пылесос, газонокосилка) как опциональные плагины 1C сверх заглушки;
  при отсутствии интернета плагин не активен (offline-first).
- 🔬 **Имитация присутствия** (Away Lighting: рандомизация света в отъезде) — дешёвая фича к режиму Vacation поверх
  presence (3D) + сцен (3B); **можно подтянуть раньше** (в Фазу 3/4) как быстрый win.
- 🔬 **Федерация инстансов** (Hubitat Hub Mesh: устройства/режимы/переменные шарятся между хабами в LAN) — дальний
  прицел для больших домов/дач.

---

### Эпик 2Q. Обобщённые блоки: примитивы + PID + скрипт-блок + шаблоны ✅ (Фазы 0–4 реализованы, ветка `epic-2p-ml-tasks`; НЕ прогонялось вживую)

> **✅ Реализовано (Фазы 0–4, не прогнано вживую vs RabbitMQ/Mongo).** Платформа: типизированные `Options`/опц. порты (default-члены интерфейсов), катал. DTO + TS-контракт. **25 обобщённых блоков** в `Blocks/GenericBlocks.cs` + `TimeStateBlocks.cs` + `PidBlock.cs` + `ExpressionBlock.cs`: comparator, ⭐hysteresis, window, logic, select, linear_map, clamp, deadband, rate_limiter, aggregate, min_dwell, on_delay/off_delay, pulse, interval, edge, latch, counter, sample_hold, debounce, moving_average, median_filter, ramp, pid (feedforward+пресеты+анти-windup), expression (свой вычислитель `Expressions/ExpressionEngine.cs`, ~350 строк, 0 зависимостей). Источник `ml_predictor`; дедуп governor'а в `MlGovernorCore`+`DriftWindow`. **Персистентность состояния** (`block_state` коллекция + `BlockStateStore` + `StateCoerce` + snapshot/restore в `BlockRuntime`). DSL string-опции + `CompositeNode.Options`; шаблоны-композиты (smoothed_sensor, cooling_relay, smart_thermostat). WebUI: секция Options (enum/bool/script) в форме блока. **110 .NET блок-тестов + 96 WebUI-тестов зелёные.** Остаток (хвост): проброс параметров композита наружу (шаблоны пока с baked-дефолтами, как climate_loop), PID-автотюнинг, галерея шаблонов в UI, live-прогон.
>
> **Проблема.** Текущий набор блоков переспециализирован (`thermostat`, `co2_ventilation`, `sun_gate`, `irrigation_sequencer`). Одни и те же идеи (гистерезис, порог, таймер, min-dwell) переписаны вручную в каждом. Обобщённый ровно один — `ewma_filter`. Нужен слой **примитивов** (по образцу EMA: любой вход → обработка → выход, привязываемый к устройству/другому блоку), из которых пользователь собирает любые контуры; поверх — понятные **шаблоны** (готовые рецепты), чтобы не собирать с нуля.
>
> **Два включающих изменения платформы (Фаза 0).** (1) **Типизированные `Options`** — `ControlBlock.Options: Dictionary<string,string>` + `BlockOptionSpec(Name, Kind: enum/bool/text, Values, Default)` в SDK + `IBlockContext.Option(key)`; сейчас параметры только `double`, из-за чего оператор компаратора/логики/агрегатора негде хранить. Аддитивно, через default-члены интерфейсов (не ломает существующие блоки/фейки). (2) **Опциональные порты** — флаг `BlockPortSpec.Optional` (для второго входа PID `ff`, `inhibit` и т.п.). (3, отложено в Фазу 4) проброс `Options` в узлы композита + расширение DSL, иначе шаблоны из option-блоков не собрать.
>
> **Каталог примитивов (по слоям обработки).**
> - **Источники:** `constant`, `setpoint` (writable — уставка, которую двигают UI/сценарий/ML), `ml_predictor` (предиктор-делегат как блок-источник).
> - **Кондиционирование (Number→Number):** `ewma_filter` ✅, `moving_average`, `median_filter`, `rate_limiter` (slew), `deadband`, `debounce`, `sample_hold`.
> - **Математика:** `linear_map` (gain/offset), `clamp`, `aggregate` (min/max/sum/avg), `expression` (скрипт, см. ниже).
> - **Сравнение/логика:** ⭐`hysteresis` (обобщённый релейный из примера — заменяет гистерезис в термостате/CO₂/toggle), `comparator`, `window`, `logic` (and/or/xor/nand/nor), `select` (mux), `priority`.
> - **Время/состояние:** `on_delay`/`off_delay` (TON/TOFF), `pulse`, `interval`, `edge`, `latch` (SR), `min_dwell` (анти-дребезг), `counter`, `time_gate`.
> - **Регулирование:** `hysteresis`, ⭐`pid`, `ramp`.
> - **ML-надстройка:** обобщённый `ml_governor` (стадии Shadow→Bounded→Full + drift + clamp вокруг любого предложенного значения; убирает дубль `MlSelectorGovernor`).
>
> **PID (в v1, а не поздняя фаза).** Выход = мощность/позиция 0..100 % (диммер/клапан/ПЧ/ТЭН-ШИМ) — основа энергосбережения и «умнее ML» (модель предлагает **уставку**, PID отрабатывает по мощности). Обвязка обязательна: анти-windup (back-calculation), клампы `outMin/outMax`, derivative-on-measurement (без kick), безопасный дефолт при пропаже `pv`, `dt`-корректность. **Опциональный второй вход `ff` (feedforward)** — учёт улицы/возмущения (`out = clamp(PID(sp−pv) + ffGain·ff)`); погодозависимость/каскад/много входов — композицией (`linear_map`/`expression → pid`), а не портами. **Пресеты-профили** вместо сырых kp/ki/kd для обычного пользователя: 🕊️ Мягкий / ⚖️ Сбалансированный (деф.) / ⚡ Быстрый / 🌱 Экономный / 🔧 Вручную — карточки с человекочитаемым описанием; конкретные числа — из таблиц под применение (живут в шаблоне). **Автотюнинг** (релейный тест) — отдельной поздней фазой; `preset` спроектирован так, чтобы автотюн стал ещё одним источником чисел.
>
> **Скрипт-блок `expression` (кастомные блоки псевдоязыком).** Собственный **крохотный вычислитель** (рекурсивный спуск, ~300–500 строк, 0 зависимостей — не задевает лицензионное правило; НЕ Roslyn/JS-движок). По построению не может стать «супер-языком»: нет циклов/определений функций/присваиваний в состояние. Multi-line: строки `OUTn = <expr>` и `let tmp = <expr>`; входы `IN1..INk`; выходы `OUT1..OUTm` (Number/Bool). Операторы `+ - * / %`, сравнения, `&& || !`, тернарник; функции-белый-список (`min max abs clamp round floor ceil sqrt pow avg`); спец-встроенные `prev(OUTn)` и `dt` (фильтр/гистерезис в одну строку). Жёсткие лимиты (строк/узлов AST), парсинг один раз → кеш AST, `NaN/Inf`→удержать/дефолт. Сохраняется и как **именованный кастомный тип** (в Mongo рядом с композитами). Компромисс: блок непрозрачен для графа/объяснимости (1F) → позиционируется как escape-hatch.
>
> **Шаблоны (Фаза 4).** Доменные блоки становятся рецептами-композитами из примитивов с вынесенными параметрами: Термостат (реле), Термостат ПИД (мощность), Погодозависимый термостат (`pid`+ff+кривая), Вентиляция по CO₂/влажности, Освещение (солнце/время/присутствие), Полив с блокировкой по дождю, Сглаженный датчик, Энергоменеджер (лимит мощности), ML-регулятор. Старые `TypeId` — алиасы шаблонов (существующие инстансы не ломаются). В UI — галерея шаблонов + «разобрать на примитивы».
>
> **Фазы и DoD.** **Ф0** — платформа (Options/Optional/`Option()`, катал. DTO, TS-контракт). **Ф1** — stateless-примитивы (comparator, hysteresis, linear_map, clamp, logic, select, window, aggregate, deadband, rate_limiter, min_dwell) + `pid`, юнит-тесты. **Ф2** — время/состояние (delay/pulse/interval/edge/latch/counter/moving_average/median/sample_hold) + персистентность `State` между рестартами. **Ф3** — `expression`-движок + `ramp` + обобщённый `ml_governor` + `ml_predictor`-источник. **Ф4** — шаблоны + WebUI (форма Options, редактор скрипта, галерея шаблонов). DoD каждой фазы: `dotnet test` зелёный + WebUI build/тесты, где затронут UI.

---

## Сводная карта фаз

| Фаза | Горизонт | Ключевой результат | Главные компоненты |
|---|---|---|---|
| **0. Фундамент** | ✅ заложен | ядро пригодно к росту | контракт, capability-модель, зоны, event-log (Mongo TS), снятие auth-трения + чистка |
| **1. Автоматизация** | ✅ функц. закрыта | дом работает по сценариям; копятся данные; действия объяснимы | AutomationService (1A ✅), режимы дома+присутствие (1G ✅), платформа данных телеметрии (1B ✅), объяснимость+реплей (1F ✅), control blocks (1H ✅ E1), climate/heating/irrigation контуры (1D ✅), Integration SDK (1C ✅ супервизор), визуальный flow-редактор (1E ✅) |
| **1.5 Верификация** | ✅ закрыта | стек проверен вживую | ✅ интеграционный data-path тест (P0-4) против реальных RabbitMQ/Mongo (Testcontainers); ✅ offline-smoke в CI (GitHub Actions); ✅ живой 3-оконный прогон с реальным Zigbee |
| **2. Интеллект** | ✅ эпики закрыты (v1) | дом подсказывает; сам ищет закономерности; умнее распознаёт устройства; читаемый центр активности | типизация устройств (2D ✅), центр активности (2G ✅), ML-субстрат на ML.NET (2A ✅), ML-термостат shadow→bounded→full (2B ✅), очередь предложений (2C ✅), роли (2E ✅), движок поиска закономерностей (2F ✅ v1 тип A), LLM-заглушки (2H ✅), **адаптер ESPHome через MQTT (2J ✅)**, геолокация+настройки (2K ✅), платформенные вирт. сенсоры Sun/Time/Calendar (2L ✅), commute-плагин (2M 🟡 2M.1 ✅; 2M.2 = LAN-канал SignalR ⬜), панели-пульты/мониторы (2O 🔵 план) |
| **3. Продуктовая зрелость** | ⬜ ближайшее (фундамент готов) | продукт, которому доверяют ежедневно и который переживает катастрофу (Приоритет-1 исследования) | **бэкапы/восстановление/миграция (3A ⬜, пробел №1)**, сцены first-class (3B ⬜), энергодомен+тариф+«дешёвые часы» (3C ⬜), per-person presence (3D ⬜), движок правил wait/on-error/переменные (3E ⬜), дисциплина уведомлений (3F ⬜), UX редактора правил (3G ⬜), абстракция хранилища / выбор БД (3H ⬜, шов + PostgreSQL-кандидат) |
| **4. Домен-расширения** | ⬜ среднесрочно | расширение вширь: охрана, свет, энерго-аналитика, сеть, план дома (Приоритет-2) | иерархия локаций (4A ⬜), охранный домен Alarmo/HSM+протечка→кран (4B ⬜), циркадный свет (4C ⬜), ML энерго-аномалии (4D ⬜), карта топологии сети (4E ⬜), community-шаблоны URL/QR (4F ⬜), план дома как дашборд (4G ⬜), persistence-фильтры (4H ⬜) |
| **5. Автономность** | долгосрочно / концепт | самоуправление + голос + видео + доступ + энергооптимизация | **полная безопасность + умные замки + гостевой доступ** (из Ф2), Matter/Thread (matter.js, Thermostat Suggestions), локальный RU-голос, LLM-слой (дозревание 2H), видео/Frigate-камеры, active-ML, энергооптимизация (EMHASS/MPC), облачные плагины, имитация присутствия, федерация инстансов |

## Сквозные направления (на всех фазах)

- **Наблюдаемость:** Prometheus-метрики на каждый сервис/плагин, health/readiness; операционные логи через Serilog (сейчас консоль; опционально Mongo-sink) — отдельно от доменного event-log. **Трассировка решений** автоматизаций/ML (какое правило/модель и по какому триггеру совершило действие) — часть доменных данных, питает объяснимость (Эпик 1F), НЕ операционные логи. **С 2026-07-19 Prometheus-контур (prometheus + mongodb-exporter) — opt-in compose-профиль `monitoring`, по умолчанию выключен** (Grafana/алертов нет — собранное сейчас никто не смотрит; `/metrics`-эндпоинты в сервисах остаются, доустановка = `--profile monitoring`; см. [план развёртывания](../deployment_plan_ru.md)).
- **Тестирование:** интеграционные тесты на сквозные потоки (discovery, команда, state, срабатывание правила) — растить покрытие с Фазы 0. **Offline-smoke:** тест «всё ядро (discovery/команда/state/правило) работает при отключённой внешней сети» — гарантия инварианта offline-first (принцип 2), а не декларация.
- **Устойчивость:** реконнект к шине, восстановление состояния из БД, graceful degradation при недоступных зависимостях.
- **Аварийное восстановление (DR):** бэкап/восстановление/миграция как первоклассная возможность продукта (Эпик 3A) — «хаб умер → дом умер» это страх №1 self-hosted; полный локальный bundle (данные + конфиги + настройки плагинов + Zigbee-координатор), off-site опционально. Это принцип 5 (fail-safe) на уровне продукта, а не только рантайма.
- **Дисциплина уведомлений:** таксономия reactive/proactive/optimization, per-type каналы, rate-limiting/dedup против «cry wolf» (NN/g), actionable-кнопки с атрибуцией актора — закладывается с первого канала доставки (Эпик 3F, поверх 2M.2/2O), а не наращивается постфактум.

## Принятые решения (зафиксировано с владельцем)

1. **Железо — homelab** (мини-ПК → полноценный ПК → стойка), не промышленное; сервер совмещён с хранилищами и др. задачами. Отсюда **resource-aware модульность**: тяжёлые плагины/компоненты включаются при наличии мощностей, иначе — базовый функционал.
2. **Телеметрия и доменный event-log — MongoDB time-series** (единая БД; отдельный TSDB не вводим). Mongo изначально выбран как хранилище объектов с поддержкой time-series.
3. **Брокер — RabbitMQ** (закрывает и шину, и MQTT-брокер; NATS не нужен). **Event-log пишем в Mongo** (доменные данные для ML/аудита), Serilog — только для операционной диагностики.
4. **Авторизация — локальная и отложенная.** JWT на стадии разработки убираем как мешающий артефакт; позже — локальная авторизация «поверх» шлюзов/UI для контроля пользователей и внешних процессов. **Вся система обязана стабильно работать локально без внешней сети**; облачные интеграции — опциональны.
5. **ML — через ML.NET и как блоки 1H, не Python-пайплайн (ревью плана Фазы 2).** Аналитический ML (уставки, прогноз, аномалии, типизация) — на ML.NET, переиспускает рантайм control-blocks; модель — это блок (вход=фичи → выход=уставка). Python — только для будущих видео/голоса (Фаза 5). **Апрувится версия модели + уровень полномочий, не отдельные выходы**; живой поток под клампами безопасного пола + стадийный выкат Shadow→Bounded-Active→Full. **Полная безопасность доступа + умные замки перенесены в Фазу 5** (auth мешает разработке сейчас, замки нельзя без auth). Перед Фазой 2 — **Фаза 1.5** (живая верификация рантайма).
6. **Перегруппировка forward-фаз по рыночному исследованию (owner, 2026-07-19).** После закрытия Фазы 2 (интеллект-инфраструктура) [deep-research рынка 2025–2026](../../memory-bank/marketResearch2026.md) выявил, что продукту не хватает **базовой домовладельческой зрелости** (бэкапы, сцены, энергодомен, per-person presence, зрелый движок правил, дисциплина уведомлений) — есть у всех конкурентов, у нас нет, фундамент готов. **Правило правок:** закрытые фазы (0/1/1.5/2) не трогаем (только хвосты); forward-часть перегруппирована и переномерована по приоритетам исследования: **Фаза 3 = Приоритет-1** (продуктовая зрелость/доверие), **Фаза 4 = Приоритет-2** (домен-расширения), **Фаза 5 = Приоритет-3 + автономность** (бывшая Фаза 3, контент 1:1, сдвинут номер). Ссылки в закрытых эпиках на «Фазу 3» (auth/замки/видео/голос) читаются как **Фаза 5**. **Бэкапы (3A) — пробел №1** и первый кандидат к исполнению.
7. **Боевой запуск малого дома владельца — отдельный план развёртки, НЕ эпик карты (owner, 2026-07-19,
   уточнено).** Переезд с HA (Raspberry Pi 4 + Arduino Mega/Nano) на Domovoy — это **операционная активность,
   а не то, что «строим»**, поэтому вынесен из дорожной карты в отдельный [**план развёртывания**](../deployment_plan_ru.md)
   (требования к железу + предусловия карты, которые должны быть закрыты до боя + правки compose + миграция).
   Ключевое, что осталось в решениях: **сервер — мини-ПК x86** (min: 4 ядра с AVX / 8 ГБ / SSD 64 ГБ;
   рекомендовано: N100+ / 16 ГБ / NVMe 256 ГБ), **Pi 4 исключён** (официальный MongoDB ≥5.0 не работает на
   Cortex-A72 — нет ARMv8.2-A, а фичи 1B требуют ≥5.0/6.3); **Prometheus/exporter → opt-in профиль
   `monitoring`** (по умолчанию выкл — собранное сейчас никто не смотрит). Детали и чек-лист — в плане развёртки.
8. **Абстракция хранилища: Mongo остаётся default, но вводится шов выбора БД (owner, 2026-07-19; дополняет
   решение №2, не отменяя его).** Пользователям — выбор БД в духе HA recorder (Эпик 3H). Вся Mongo-сцепка
   уже локализована в DbGateway (+ Serilog-sink) → шов = доменные store-интерфейсы внутри DbGateway (Ф0–Ф1),
   затем коннектор **PostgreSQL** (JSONB + `date_trunc`; НЕ TimescaleDB — лицензия TSL) как первая
   альтернатива (Ф2); SQLite/MariaDB — только по спросу. Матрица поддержки ограничена Mongo + PostgreSQL
   (каждый бэкенд = постоянная цена в тестах). Переход самоцелью не является; сейчас — только
   проектирование/шов.

---
*Живой документ. Обновляется по мере прохождения фаз; детализация дальних фаз растёт по мере приближения.*
