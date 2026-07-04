# Анализ проекта Domovoy

**Дата**: 2026-07-03
**Версия**: 2.0 (полностью заменяет отчёт от 2026-04-29 — тот описывал систему до Phase 1/2)
**Срез**: ветка `epic-2b` (Phase 1 закрыта, Phase 2: эпики 2A/2B/2D/2G/2I выполнены, 2C запланирован)

---

## 1. Общая оценка

Проект в хорошей форме для своей стадии. Капабилити-архитектура (единый контракт устройств, адаптеры протоколов, read-model + append-only event-log) выдержана последовательно; ML-подсистема — стейджинг Shadow→Bounded→Full, дрифт-монитор с авто-демоцией, зональный скоупинг моделей (zone→zone_kind→global), честный хронологический holdout — спроектирована грамотно и покрыта юнит-тестами (61 тест, все зелёные).

Основные долги сосредоточены в трёх местах:

1. **Инфраструктурный слой** — подключение к RabbitMQ, индексы Mongo, retry/backpressure (частично закрыто 2026-07-03, см. пометки «✅ исправлено»).
2. **Дублирование** — 8 копий проксирующего паттерна в ApiGateway; независимые поллинги одних и тех же эндпоинтов с пяти страниц WebUI.
3. **Интеграционное тестирование** — оркестрация ML-обучения, актуация block-runtime и SignalR-плечо не покрыты E2E.

---

## 2. Неэффективности кода

### 2.1. Высокий приоритет

| Место | Проблема | Статус |
|---|---|---|
| `src\Common\Domovoy.MessageBus\RabbitMqConnection.cs` | Блокирующие `.Result` в конструкторе (зависание сервиса при недоступном брокере) и `.Wait()` в `Dispose` (зависание shutdown) | ✅ исправлено 2026-07-03: ленивое подключение с retry/backoff, авто-восстановление соединения, `IAsyncDisposable` |
| Вся кодовая база DbGateway | Ни одного `CreateIndex`: фильтрующие запросы `/api/events` и `/api/telemetry` — full-scan на растущих коллекциях | ✅ исправлено 2026-07-03: составные индексы в `TimeSeriesInitializer` (`Meta.DeviceId/ZoneId/CapabilityId + Timestamp`, `auto_history(RuleId, Timestamp)`) |
| `RabbitMqConnection.SubscribeAsync` | Нет `BasicQos` (неограниченный prefetch) и nack с requeue — «ядовитое» сообщение крутится вечно | ✅ исправлено 2026-07-03: prefetch 50, dead-letter exchange `domovoy.dlx` + очереди `<queue>.dlq` |
| `Ml\MlTrainingService.cs` | Жёсткий лимит 5000 сэмплов при обучении — плотная телеметрия молча обрезалась, holdout-метрики недостоверны | ✅ исправлено 2026-07-03: пагинация окна обучения (страницы по 5000, потолок 100 000 с предупреждением в лог) |
| `EventInterceptor.cs:166` | N+1: на каждый state report точечный запрос старого состояния из `capability_devices`; напрашивается in-memory кэш последнего состояния | открыто |
| Контроллеры ApiGateway (8 шт.) | Полная буферизация тел ответов (`ReadAsStringAsync`) — CSV-экспорт телеметрии грузится в память целиком дважды | открыто |
| `RuleRunner.cs:33-44`, `Zigbee2MqttAdapter.cs:82` | Fire-and-forget `Task.Run` — исключения проглатываются, при shutdown задачи-сироты | открыто |
| `AutomationScheduler.cs:36,89` | `DateTimeOffset.Now` вместо `UtcNow` — переходы DST дают пропущенные тики расписаний | открыто |
| WebUI: Devices/Flow/Blocks/Logs/Plugins | Пять страниц независимо поллят `/api/capability-devices` (5–30 с) без общего кэша и дедупликации, при живом SignalR-пуше | открыто |
| WebUI: `DeviceTile.tsx` | Нет `React.memo` — сетка из 50 устройств перерисовывается целиком на каждый полл | открыто |

### 2.2. Средний приоритет

- Bare `catch` в `MetricsController.cs:61-76` — одна упавшая метрика обнуляет все поля ответа; в WebUI — тихие `catch → toast` без логирования.
- Аллокации на каждом чтении: `RuleStore.cs:82` (`Concat().ToList()` каждые 5 с), `ZigbeeBridgeStateCache.cs:81` (`.ToList().AsReadOnly()` на каждый вызов).
- Нет валидации входа: имя правила без лимита длины (`AutomationEndpoints.cs`), `DelaySeconds` без проверки переполнения (`ActionExecutor.cs:70-74`).
- `EventRelayService.cs:34-40` — подписка не завершается до выхода из StartAsync (скрытая гонка на старте).
- Мутация общего состояния `events?.Reverse()` в `DbGatewayClient.cs:116`.
- `ReplayService.cs:64-65` — реплей молча обрезается на 5000 событий без уведомления клиента.
- WebUI: `useEffect` с нестабильными колбэками в зависимостях (Devices, Zones, Models, Plugins); монолитные страницы 440–500 строк (Flow, ZigbeeDevices, Automations) с инлайн-диалогами; динамический `import` внутри обработчика (`ZigbeeDevices.tsx:300`).

---

## 3. Неэффективности архитектуры

1. **DbGateway — единая точка отказа и двойной хоп.** AutomationService тянет телеметрию, правила, устройства и режим по HTTP каждые 30 с (`RefreshLoop.cs`) без retry и circuit breaker; при недоступности DbGateway движок молча живёт на устаревших правилах. Polly/`Microsoft.Extensions.Http.Resilience` в проекте нет.
2. **Избыточная фрагментация для single-box хаба.** 6 сервисов + 5 инфраструктурных контейнеров на одной машине. Декомпозиция оправдана для адаптеров (Connectivity, PluginSupervisor), но DbGateway + AutomationService + UnifiedDeviceService — по сути одно ядро, разрезанное HTTP-границами.
3. **Двойная обработка состояния**: UnifiedDeviceService и EventInterceptor независимо подписаны на `DeviceStateReportV1`; UnifiedDeviceService в основном переизлучает событие для SignalR-релея.
4. **Хранилище без контроля роста**: `sensor_readings` по умолчанию вечно (RawRetentionDays=0), `device_events`/`auto_history` без TTL, ML-артефакты inline base64 без проверки 16 МБ BSON-лимита.
5. **Слабые health-checks и наблюдаемость**: compose проверяет DbGateway через `pgrep`; кастомных метрик Prometheus нет (rule fires/min, ingest rate, ML latency); Serilog пишет только в ту же Mongo — при её падении пропадают и логи об этом.
6. **Безопасность (осознанно отложена до Phase 3)**: API без аутентификации, CORS `SetIsOriginAllowed(_ => true)` + `AllowCredentials`, дефолтные креды RabbitMQ в `.env`, JWT-секрет-заглушка в `appsettings.json`, Mongo без авторизации. Для LAN-only приемлемо; ужесточение CORS — бесплатно уже сейчас.
7. **Контракты шины**: версионирование `.v1`-суффиксами — рабочее; схемной валидации нет (ошибки десериализации теперь уходят в DLQ, а не в бесконечный requeue).

---

## 4. Идеи развития — кодовая база

В порядке «эффект / трудозатраты»:

1. ~~Инфраструктурный спринт~~ — ✅ выполнен 2026-07-03 (индексы, async-инициализация RabbitMQ + QoS/DLQ, пагинация ML-выборки).
2. **Устранить дублирование прокси в ApiGateway** — общий `ProxyForwardingService` со стримингом тела или переход на YARP (8 контроллеров → таблица маршрутов, стриминг и retry из коробки).
3. **Resilience-слой**: `Microsoft.Extensions.Http.Resilience` на все HttpClient; настоящие health-checks (`AddMongoDb`/`AddRabbitMQ`) вместо `pgrep`; кастомные метрики Prometheus в ключевых точках.
4. **WebUI — слой данных**: TanStack Query как единый кэш + дедупликация (закрывает разом все независимые поллинги); `React.memo` на тайлы; вынести диалоги из монолитных страниц.
5. **Закрыть тестовые дыры** (Testcontainers-каркас уже есть): E2E `MlTrainingService.TrainOnceAsync` (fetch→train→register→load), актуация block-runtime (governor тикает → команда в шину), SignalR-плечо.
6. **К Phase 3 — консолидация ядра**: DbGateway + AutomationService + UnifiedDeviceService в один процесс с модульными границами (in-process интерфейсы вместо HTTP); отдельными остаются Connectivity и PluginSupervisor. Минимум — ADR с обоснованием текущей топологии.
7. **Гигиена хранилища**: разумный дефолт RawRetentionDays (rollups из 1B уже есть), TTL на `auto_history`, проверка размера ML-артефакта перед записью и план перехода на GridFS при росте моделей.

---

## 5. Идеи развития — функционал

**Ближнее (усиливает построенное):**

1. **Epic 2C (proposal/approval queue)** — следующий по плану; per-instance выбор модели снимет ограничение «один latest на всех» и придаст смысл зональному скоупингу 2I на уровне UI.
2. **Phase 4 из 2I — feature enrichment**: сейчас модели видят только hour+dow («выученное расписание»); occupancy/mode/наружная температура как фичи — качественный скачок ценности ML-термостата. `FeatureLocality` спроектирована и протестирована, осталась интеграция в шаблоны.
3. **Scorecard-дэшборд эффективности ML**: «модель vs baseline за неделю» — срабатывания Bounded-клэмпа, демоции, MAE-тренд по версиям. Прямой инструмент решения «повышать ли authority stage».
4. **Каналы доставки для Activity Center** (отложены из 2G): push/Telegram для дрифт-демоций и падений плагинов.

**Среднее (новая ценность):**

5. **Энергетика как first-class домен**: учёт потребления, ML-шаблон прогноза, блоки сдвига нагрузки на дешёвый тариф — сильный дифференциатор и идеально ложится на схему «модель предлагает setpoint — детерминированный контур исполняет».
6. **ML→rule-proposals** (вторая половина 2C): «вы всегда выключаете свет в 23:30 — создать правило?» через ту же очередь одобрения.
7. **Сцены/снапшоты состояния зоны** с привязкой к режимам 1G — дёшево поверх капабилити-модели.
8. **Vacation-режим с имитацией присутствия**: проигрывание уже выученных schedule-моделей с шумом — переиспользование ML-подсистемы почти бесплатно.

**Дальнее (задел Phase 3):**

9. **Локальная авторизация раньше замков**: минимальный логин на ApiGateway + reverse-proxy TLS — разблокирует безопасный удалённый доступ до полного auth-эпика.
10. **Резервное копирование/экспорт конфигурации** (правила, блоки, зоны, модели) одним архивом — критично для доверия к self-hosted продукту.

---

## 6. Что исправлено 2026-07-03 (инфраструктурный спринт)

1. **`RabbitMqConnection`** (`src\Common\Domovoy.MessageBus`): ленивое асинхронное подключение с экспоненциальным retry (до 6 попыток), `AutomaticRecoveryEnabled`/`TopologyRecoveryEnabled` для рестартов брокера в рантайме, `BasicQos` prefetch 50, dead-letter exchange `domovoy.dlx` с очередями `<queue>.dlq` (с откатом к старой декларации для очередей, созданных прежней версией), ack/nack с `CancellationToken.None` (обработанное сообщение не передоставляется при shutdown), `IAsyncDisposable` + ограниченный по времени синхронный Dispose, кэш деклараций exchange'ей.
2. **Индексы Mongo** (`TimeSeriesInitializer.EnsureIndexesAsync`): `device_events` — (Meta.DeviceId, Timestamp), (Meta.ZoneId, Timestamp), (CapabilityId, Timestamp); `sensor_readings` — (Meta.DeviceId/ZoneId/CapabilityId, Timestamp); `auto_history` — (RuleId, Timestamp), (Timestamp). Идемпотентно, ошибки — warning в лог (не-meta поля time-series требуют MongoDB 6.3+).
3. **Пагинация ML-обучения** (`MlTrainingService.LoadPagedAsync` + параметр `toUtc` в `DbGatewayClient`): выборка окна обучения страницами по 5000 (newest-first, сдвиг верхней границы), потолок 100 000 сэмплов с предупреждением, итог сортируется хронологически.

Проверка: `dotnet build` без ошибок; 61/61 тестов зелёные, включая интеграционные (реальные RabbitMQ + Mongo через Testcontainers).
