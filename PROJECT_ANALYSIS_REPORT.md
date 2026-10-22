# Анализ проекта Domovoy

**Дата**: 2026-08-07
**Версия**: 3.0 (полностью заменяет отчёт от 2026-07-03 — тот описывал срез до Фазы 3)
**Срез**: ветка `epic-3k-delivery` (`a42348a`; Фазы 0–3 закрыты, Эпик 3K реализован, живой прогон не выполнялся)
**Фокус**: мёртвый/неиспользуемый код, запутанная логика, читаемость. Три зоны: backend-сервисы и шлюзы (258 файлов, ~35 400 строк), общие библиотеки/контракты/тесты/сборка (~18 900 строк), WebUI (206 файлов, ~26 350 строк).

---

## 1. Общая оценка

Кодовая база в форме заметно выше средней для проекта такого объёма и темпа. Ключевые наблюдения:

- **Чистка удалённых фич выполнена аккуратно.** Страница Flow, блоки Powercalc/интегратора, legacy-путь устройств (Step 5) — вычищены полностью: grep по всему репозиторию находит только поясняющие упоминания в комментариях и доках. Пункты отчёта 2.0 про Flow-монолиты и legacy-баги закрыты жизнью.
- **Контракты чистые.** `Domovoy.Contracts` не имеет ни одного PackageReference и не зависит от `Domovoy.Common`; все 12 констант `MessageTypes`, все payload-record'ы и все члены `BusTopology` имеют и издателя, и потребителя. Версий `.v2`, вытеснивших `.v1`, нет — мёртвых старых версий сообщений не осталось.
- **Дисциплина комментариев и именования высокая**: почти каждый класс несёт XML-doc со ссылкой на эпик, магические числа в основном вынесены в именованные константы с объяснением. `Domovoy.Narrative`, `LoadManager`/`LoadShedPlanner`, `build/tools/contract-guard.sh` и `spec.mjs` — образцовые.
- **Мёртвого кода мало и он локализован**: суммарно ~1 000 строк на ~80 000 (около 1 %), причём большая часть — концентрированные «замкнутые островки», удаляемые без риска (раздел 3).
- **Реальный долг — не в мёртвом коде.** Он в трёх местах: (а) поведенческие дефекты, часть из которых критична для предстоящего живого прогона 3K (раздел 2); (б) незавершённые миграции, оставившие полуслои (шов 3H, UnifiedDeviceService после Step 5, хвосты 3K); (в) систематическое дублирование — 23 прокси-контроллера ApiGateway и отсутствие общего слоя данных в WebUI (раздел 4).

---

## 2. Подтверждённые дефекты (поведенческие)

Упорядочены по серьёзности. Первые два пункта проверены построчно повторно, остальные — по grep-доказательствам аудита.

### 2.1. Критичные для живого прогона 3K

| # | Дефект | Место | Суть |
|---|---|---|---|
| 1 | **Бэкап перед обновлением никогда не создаётся** | `src/Services/Domovoy.Updater/Services/UpdateExecutor.cs:340` | Вызов идёт на `/api/backups/run`, а DbGateway маппит `/api/backup/run` (`BackupEndpoints.cs:28` — `MapGroup("/api/backup")`, `:53` — `MapPost("/run")`). 404 глотается в `RequestBackupAsync` с warning и `null` — при `BackupBeforeUpdate = true` (дефолт) страховочный бэкап тихо не создаётся. Дополнительно `?reason=pre-update` игнорируется: эндпоинт хардкодит `CreateBackupAsync("manual")`. |
| 2 | **`UseMqtt = true` по умолчанию → 6 из 8 сервисов работают в MQTT-режиме шины** | `src/Common/Domovoy.MessageBus/RabbitMqConfig.cs:45` | `UseMqtt: false` задан только в appsettings AutomationService и UnifiedDeviceService; в compose переменной `RABBITMQ__USEMQTT` нет нигде. Следствия в `RabbitMQConnection.cs`: на каждое сообщение — заголовки `mqtt-qos`/`mqtt-retain` (153–160); тихая подмена routing key `#`→`*` (201–204); аргумент `mqtt-subscription-qos` в объявлении очереди (190–196), из-за которого при несовпадении аргументов срабатывает fallback «declaring without dead-lettering» (274–284) — **DLQ незаметно отключается**. |
| 3 | **Настройки «проверять обновления» из UI ни на что не влияют** | `src/Services/Domovoy.Updater/Services/UpdateCheckService.cs:47,70` | Служба читает `CheckEnabled`/`CheckIntervalHours` только из env (`UpdaterOptions`); из БД берётся только `Channel`. Тумблер и интервал в `/settings` — декорация до перезапуска контейнера с другим env. |
| 4 | **`lastCheckResult` в UI — вечный `null`** | `UpdateSettingsEndpoints.cs:62-68`, `IUpdateStore.RecordCheckAsync`, `MongoUpdateStore.cs:46` | Эндпоинт `POST /api/update-settings/check-result` («пишет служба обновлений») никем не вызывается — `UpdateCheckService` отметку о проверке не пишет. Поля `UpdateSettings.LastCheckAt`/`LastCheckResult` всегда пусты, UI (`api/updates.ts:23`) ждёт их зря. |
| 5 | **Уведомление об обновлении обходит дисциплину 3F** | `UpdateCheckService.cs:117` | Публикация `NotificationRaisedV1` идёт напрямую в exchange (выход `SignalRChannel`), минуя `NotificationDispatcher`/`NotificationPolicy`: mute-каналы, rate-limit, dedup и маршрутизация по категориям не применяются; в ntfy/Telegram/webhook уведомление не уйдёт никогда. XML-doc класса при этом заявляет обратное («Notification discipline (Epic 3F) applies»). |
| 6 | **Плагин выпал из конвейера поставки** | `build/components.json`, `build/tools/contract-guard.sh:36`, `.gitignore:19-21` | `src/Plugins/**` и `src/Tools/**` не входят ни в один компонент и ни в `sharedPaths`: правка CommutePlugin даёт пустую build-матрицу, DLL плагина не попадают ни в один образ (доставка — только host-volume `./plugins`), contract-guard изменения плагина не видит. Изменение плагина физически не доедет до дома каналом обновлений. |
| 7 | **Тесты CommutePlugin не запускаются в CI** | `.github/workflows/ci.yml:61,96` | Оба шага `dotnet test` указывают только `Domovoy.IntegrationTests.csproj`; `Domovoy.CommutePlugin.Tests` (269 строк, в т.ч. единственные тесты `PluginSettingsService.Coerce`) собирается, но никогда не выполняется. Лечится фильтром по решению: `dotnet test Domovoy.sln --filter "Category!=Infra"`. |

### 2.2. Прочие подтверждённые дефекты

- **`PluginSettingsService.Coerce<T>` — недостижимая ветка для `string`** (`src/Common/Domovoy.MessageBus/PluginSettingsService.cs:150-170`): дискриминатор `default(T) switch { string => ... }` не срабатывает (для `string` `default(T)` = `null`, а `null` не сопоставляется с типовым паттерном) — строки всегда уходят в `_ => je.Deserialize<T>()`. Практика: `Get<string>` для значения-числа падает в `catch` и возвращает fallback; задевает `CommutePlanner.Get(TomTomApiKey)`. Правильный дискриминатор — `typeof(T)`.
- **Гонка на `_wake` в CommutePlanner** (`CommutePlanner.cs:52,84,364,382-403`): `SleepInterruptiblyAsync` в `finally` делает `Dispose()` CTS, но поле `_wake` продолжает на него указывать; `Cancel()` из обработчиков шины/настроек на утилизированном CTS бросает `ObjectDisposedException` → сообщение уходит в nack/DLQ. Команда, пришедшая в момент пробуждения, теряется.
- **`SystemController.KnownServices` не содержит `domovoy-updater`** (`ApiGateway/Controllers/SystemController.cs:36-44`): служба обновлений зовёт `AddSystemControl("domovoy-updater")` и умеет самоперезапуск, но из UI не перезапускается.
- **Prometheus-метки из сырых путей** (`ApiGateway/Middleware/RequestCounterMiddleware.cs:39-59`, `RouteCounterMiddleware.cs:41-79`): оба middleware метят метрики сырым путём с GUID устройств и именами файлов — классическая бомба кардинальности; дублируют друг друга по смыслу; ApiGateway не использует `UseHttpMetrics()` (метки по route-template), хотя DbGateway использует.
- **Дефолтный JWT-секрет закоммичен дважды** (`ApiGateway/appsettings.json` + fallback в `Program.cs:81`): JWT выключен, но включение без замены ключа даёт подделываемые токены.
- **`DeviceOnlineChangedV1` до SignalR не доходит** (известный открытый пункт): события пишутся в Mongo, но consumer в realtime-плече не подключён — UI не получает online/offline пушем.
- **WebUI: счётчик через выгрузку данных** (`components/common/DomovoyDigest.tsx:32-34`): каждую минуту тянет 500 строк активности, чтобы взять `entries.length`.
- **WebUI: пересоздание WebSocket при смене языка** (`pages/Devices.tsx:150`, `pages/ZigbeeDevices.tsx:259`): функция перевода `t` попала в зависимости эффекта соединения ради строки ошибки — смена языка рвёт и переустанавливает SignalR-хаб.
- **WebUI: сырые NUL-байты в исходнике** (`components/charts/useTelemetryBatch.ts:37,68,80,82`): символ U+0000 вставлен литерально (не как escape) в ключи-шаблоны — файл детектится как бинарный, выпадает из grep/ripgrep/diff. Замена на escape `'\0'` или `'|'` семантику не меняет.
- **Fire-and-forget без наблюдаемости**: `UpdateEndpoints.cs:97,113` (`Task.Run` для Apply/Rollback — исключение до первого Persist пропадает), `EventRelayService.cs:38` и `ZigbeeBridgeStateCache.cs:110-122` (подписки без `await` и без токена остановки — ср. с корректным `NotificationRelayService.cs:35`).
- **Мёртвая конфигурация-ловушка шины** (`RabbitMqConfig.cs:60-70`): `MaxReconnectAttempts`/`ReconnectInterval`/`AutoCreateStructures` не читаются нигде — реальные ретраи зашиты константами `RabbitMQConnection.cs:30,116`. Выглядит как настройка, не является ею.
- **Wire-формат шины не зафиксирован**: `RabbitMQConnection.cs:150,219` сериализует без `JsonSerializerOptions` — payload едет в PascalCase при lower-case полях самого `Envelope` (через `[JsonPropertyName]`); одно сообщение в двух конвенциях. Всего в репозитории JSON настроен ~12 разными способами (Web-defaults в 9 местах, ручной CamelCase, case-insensitive, дефолт). `ContractsPublicApiTests` ловит C#-сигнатуры, но не сериализацию.
- **Временный триггер релиза** (`.github/workflows/release.yml:19-21`): строка `epic-3k-delivery` помечена «УДАЛИТЬ последним коммитом эпика» — держать в чек-листе мерджа 3K.

---

## 3. Мёртвый / неиспользуемый код

Каждый пункт проверен grep'ом по всему репозиторию (включая WebUI, тесты, плагины); «0 ссылок» = ни одного использования вне объявления.

### 3.1. Backend-сервисы и шлюзы

| Что | Где | Доказательство |
|---|---|---|
| Модель `UserAccess` (per-device ACL) целиком | `DbGateway/Models/UserAccess.cs` (28 строк) | 1 попадание — объявление. Права реализованы через роли (`Contracts/Security`), не через per-device grants. Вводит в заблуждение о модели прав |
| Класс `RabbitMQConfig` — дубликат-ловушка | `DbGateway/Config/RabbitMQConfig.cs` | Не биндится нигде; реальный конфиг — `Domovoy.MessageBus.RabbitMqConfig` (имя отличается регистром одной буквы) |
| `StatusController` целиком (`GET /api/status`, `/api/status/services`) | `ApiGateway/Controllers/StatusController.cs` (54 строки) | 0 вызовов из WebUI/mobile/тестов/compose/Caddy; анонимно отдаёт версию и внутренние URL. Перекрыт `/api/metrics/services` и `/api/system/services` |
| `RequestLoggingMiddlewareExtensions.UseRequestLogging` | `ApiGateway/Middleware/RequestLoggingMiddleware.cs:78-86` | Program.cs использует `UseMiddleware<>` напрямую |
| In-memory реестр `_devices` + `CapabilityDeviceRecord` — write-only | `UnifiedDeviceService/Services/CapabilityDeviceManager.cs:33,84-88,116-121` | Только записывается; читателей нет (у сервиса нет HTTP-поверхности), в исходящем событии пересобирается `report.State`. См. раздел 7 |
| Реестр устройств `ZigbeeBridgeCache` — write-only | `Connectivity/Services/ZigbeeBridgeCache.cs` (`GetDevices`, `_devices`, `ZigbeeDeviceInfo`) | DI-комментарий обещает «shared with HTTP endpoint», но HTTP-поверхности у Connectivity нет; живёт только `GetBridgeInfo()` |
| Перегрузка `BacktestAsync(int, CancellationToken)` | `AutomationService/Ml/MlTrainingService.cs:459` | «Back-compat overload», единственная ссылка — она сама |
| Перегрузка `LoadShedPlanner.PlanRestore(...)` без scope | `AutomationService/Services/LoadManager.cs:129` | В `src/` не вызывается; используется только тестами |
| Модуль `FeatureLocality` (политика spatial feature-locality) | `AutomationService/Ml/Templates/FeatureLocality.cs` | 0 вызовов из продакшн-кода: ни тренер, ни шаблоны её не применяют — живёт только ради своих тестов. Либо пометить «Фаза 4, не подключено», либо это пробел: модели учатся на кросс-зонных фичах вопреки задокументированному инварианту |
| Устаревшие XML-doc, удерживающие мёртвый код «на плаву» | `CapabilityDeviceManager.cs:24-26` (ссылка на удалённый `UnifiedDeviceManager`), `SignalRChannel.cs:15` (не тот хаб), `SettingsEndpoints.cs:17`, заголовок `EventInterceptor.cs` | Комментарии описывают архитектуру до Step 5 / до 2M.2 |

### 3.2. Контракты и общие библиотеки

| Что | Где | Доказательство |
|---|---|---|
| ~65 % `CapabilityState`: конструктор от словаря, `Has`, ярлыки `OnOff/Brightness/Temperature/Humidity/Co2/Battery`, `TryGetBool/TryGetInt/TryGetString/TryGetDouble` | `Contracts/Devices/CapabilityState.cs:20-101` | Живые потребители (`Zigbee2MqttCodec`, `Zigbee2MqttAdapter`) используют только `new`, `.Set()`, `.Values` |
| `Capability.IsReadable` + ключ `Readable` | `Contracts/Capabilities/Capability.cs:99,135-136` | Атрибут `readable` никто не выставляет — свойство всегда `true`; 0 вызовов |
| Фабрики `WellKnownCapabilities.Co2/Valve/Illuminance/Energy/Color` | `Contracts/Capabilities/WellKnownCapabilities.cs` | 0 ссылок; адаптеры собирают эти capability через общие билдеры `Number()/Boolean()`. `DayNames` — кандидат в `private` |
| Члены перечислений: `Transition.Reached`, `SunEvent.Sunset`, `VariableType.String/DateTime/List` | `Contracts/Narrative/StoryModel.cs:32`, `Contracts/Automations/*` | 0 ссылок; `TransitionResolver.Derive` не возвращает `Reached` ни в одной ветке, в языковом паке его нет |
| `RuleStatus.Approved` | `Contracts/Automations/AutomationRule.cs` | 0 ссылок, **но** enum сериализуется в BSON по порядковому номеру — удаление среднего члена молча переинтерпретирует существующие записи. Убирать только с `[BsonRepresentation(BsonType.String)]`/миграцией, либо оставить как зарезервированный слот с комментарием |
| Архетипы `person`/`presence` объявлены, но никогда не присваиваются | `Contracts/Devices/DeviceArchetypes.cs:32-33`; `DbGateway/Services/DeviceClassifier.cs:47-60` | Классификатор не имеет веток, возвращающих их: виртуальные устройства резидентов 3D получают архетип `motion` |
| Несостоявшийся cooldown в «Дневнике дома» | `Contracts/Narrative/NarrativeState.cs:21-31` (`LastUsedEntryNo`, `EntryCounter`), `Narrative/RuLanguagePackRenderer.cs:218-225`, `LanguagePack.cs:113` (`PersonaPool.Cooldown`) | Параметр `cooldown` в `PickIndex` не используется; `LastUsedEntryNo` только пишется. Фактическая логика — round-robin; документация в 3 местах и тест `Cooldown_rotates_persona...` закрепляют несоответствие. Либо реализовать, либо снести поля + переписать доки |
| Свойства `RabbitMqConfig.MaxReconnectAttempts/ReconnectInterval/AutoCreateStructures`, `RabbitMqConnection.IsMqttEnabled` | `MessageBus/RabbitMqConfig.cs:60-70`, `RabbitMQConnection.cs:48` | 0 ссылок (см. также раздел 2.2 — ловушка) |
| `LanguagePack.AvailableLocales()` | `Narrative/LanguagePack.cs:100-109` | 0 ссылок; `LanguagePackProvider` вместо него управляет потоком через `FileNotFoundException` |
| `CommuteOptions.Provider`/`.TomTomApiKey` | `Plugins/Domovoy.CommutePlugin/CommuteOptions.cs:18,21` | Вытеснены каналом настроек (`_pluginSettings.Get`); README плагина (119–126) до сих пор документирует `COMMUTE__PROVIDER`/`COMMUTE__TOMTOMAPIKEY` как рабочие env — устарело |
| PackageReference `Microsoft.Extensions.Http` | `Common/Domovoy.Common.csproj:10` | Единственное упоминание `Http` в проекте — сама строка csproj |
| Поля `BaseCommand`/`BaseEvent`: `CorrelationId`, `Parameters`, `Data`, `Success`, `Error`, `Timestamp` | `Common/Models/BaseCommand.cs`, `BaseEvent.cs` | Сами классы живые (Zigbee-bridge-канал), но из общих полей заполняется только `Source` + поля наследников; потребители остальное не читают |
| `BusTopology.PluginSettingsAppliedKeyPrefix` | `Contracts/Messaging/BusTopology.cs:64` | Используется только соседней константой — кандидат в `private const` |

### 3.3. WebUI

| Что | Где | Доказательство |
|---|---|---|
| Модуль `polling.ts` целиком (класс `PollingService` + синглтон) | `src/api/polling.ts` (204 строки) | Никогда не импортирован. Ирония: содержит ровно ту инфраструктуру дедупликации поллинга, отсутствие которой — проблема №1 фронтенда (раздел 4.4). Читает несуществующую `VITE_POLL_INTERVAL` |
| Кластер «старых логов»: `api/logs.ts` + `types/log.ts` + `types/api.ts` (`ApiResponse`, `PaginatedResponse`, `ApiError`, …) | `src/api/logs.ts`, `src/types/log.ts`, `src/types/api.ts` (~96 строк) | `api/logs.ts` не импортирован; типы импортируются только из него — замкнутый мёртвый цикл. `/logs` давно ходит в `activityApi` |
| Barrel-экспорты `store/index.ts`, `components/layout/index.ts` | там же | Не импортированы; всё берётся прямыми путями. Барель сторов к тому же ре-экспортирует 2 стора из 5 |
| `CardSkeleton` | `components/common/Loading.tsx:56-63` | 3 попадания — объявления и барель; ни одного рендера |
| Loading-срез `uiStore`: `loadingStates`, `globalLoading`, `setLoading`, `setGlobalLoading`, `isLoading` | `store/uiStore.ts:30-126` (~25 строк) | 0 вхождений вне стора |
| Экспорты `deviceZoneName`, `LoadSheddingTier`, `NotificationCategory` | `automations/ruleReadable.ts`, `api/loadManagement.ts`, `api/notifications.ts` | 0 ссылок |
| 11 мёртвых i18n-ключей (из 1898; динамические `t(\`prefix.${var}\`)` проверены и исключены) | `common:brand` (в `Navigation.tsx:88` захардкожен литерал), `common:actions.search/.retry`, `blocks:chip.disabled`, `dashboards:widgets.energy`, `dashboards:energy.topConsumers`, `devices:historyVia/historyCausedBy/aRule`, `modes:changedBy`, `automations:editor.action.waitZone` | 0 вхождений в обоих языках |
| Зависимости `date-fns`, `react-hook-form`, `fast-check` | `package.json` | 0 импортов (даты — через `Intl` в `i18n/format.ts`; формы — на `useState`). `@emotion/*` не трогать — peer-зависимости MUI |

Осиротевших компонентов, тестов и снапшотов в WebUI **нет** — граф достижимости от `App.tsx` полный; следы удалённой страницы Flow вычищены (остался один поясняющий комментарий в `RuleEditors.tsx:6`).

---

## 4. Запутанная / нелогичная логика

### 4.1. ApiGateway: 23 прокси-контроллера × копипаста `Forward` в трёх несовместимых вариантах

23 из 31 контроллера содержат приватный `Forward`/`ForwardJson`/`Proxy` (25 методов по 20–30 строк), каждый повторяет `EnableBuffering → StreamReader → ReadToEndAsync → StringContent → SendAsync → ReadAsStringAsync → ContentResult`. Query-string при этом обрабатывается тремя способами: часть контроллеров прокидывает `Request.QueryString`, `CapabilityDevicesController` собирает вручную, а 10 контроллеров (Assistant, Dashboards, Notifications, Plugins, PowerTopology, Residents, Roles, Updates, Users, Zones) **молча теряют query-string** — латентная мина: новый `?filter=` на стороне DbGateway не доедет. Буферизация тел в память ощутима для `/api/telemetry/aggregate/batch`, `/api/events` и скачивания бэкапов. Решение прежнее (из отчёта 2.0, стало только актуальнее): единый `GatewayProxy`-хелпер со стримингом и единой политикой query, либо YARP.

### 4.2. AutomationService: синхронизация состояния «всеми способами сразу»

- **`RefreshLoop`** (`Services/RefreshLoop.cs:64-84`): каждый цикл — 13 последовательных HTTP-запросов к DbGateway (rules, scenes, blocks, variables, devices, mode, location, calendar, tariff, load-mgmt, power-topology, ml, notifications) независимо от того, менялось ли что-то; 16 конструкторных зависимостей — god-object синхронизации. Комментарий в коде сам признаёт: «push-invalidation — later optimization».
- **Home mode синхронизируется трижды в одном процессе**: `HomeModeMonitor` (очередь), `SystemSensorService` (вторая подписка на то же событие, `SystemSensorService.cs:132`), `RefreshLoop.RefreshMode` (HTTP-полл поверх). Плюс четвёртая копия `_currentMode` в `EventInterceptor` (DbGateway). Авторитетный источник не определён.
- **Четыре очереди на `DeviceCommandV1` в одном процессе** (`BlockRuntime.cs:74`, `SystemSensorService.cs:114,123`, `VariableRuntimeService.cs:55`) — каждая получает весь поток и фильтрует вручную; вторая очередь на `DeviceStateReportV1` в `LoadManager.cs:252`. Внутрипроцессный `DeviceEventBroker` существует ровно для такого fan-out, но используется только для WaitForEvent/required-expression.

### 4.3. Полуслои незавершённых миграций

- **Шов 3H**: инвариант «нет `MongoDB.Driver` выше store-интерфейсов» держится в 2 файлах из ~25 (переведены `HistoryEndpoints`, `UpdateSettingsEndpoints`); `EnergyEndpoints` — гибрид, принимающий в одних обработчиках и `IMongoDatabase`, и `ITelemetryStore`. Это ожидаемое состояние Ф1 («довести ~24 домена инкрементально»), но пока полуслой создаёт иллюзию заменяемости бэкенда, которой нет.
- **UnifiedDeviceService**: единственная живая функция — перекладывать `DeviceStateReportV1` в legacy-событие `DeviceStateUpdatedEvent` для SignalR-релея ApiGateway (реестр write-only, см. 3.1). Целый контейнер и legacy-контракт ради одного hop'а. → раздел 7.
- **`IMessageBus` живёт в проекте `Domovoy.Common`, но в namespace `Domovoy.MessageBus`** — из-за этого `MessageBus` ссылается на `Common`, а плагины тянут Serilog+Mongo-sink ради одного интерфейса. Перенос файла (namespace уже правильный) + удаление неиспользуемого `using Domovoy.Common.Configuration` в `RabbitMQConnection.cs:18` разрывает зависимость полностью.
- **`RabbitMqConfig.MqttPort` шины сервисов читается адаптером брокера устройств** (`Connectivity/Services/AdapterManager.cs:95`) — два разных брокера склеены одним классом настроек.

### 4.4. WebUI: нет общего слоя данных

На простаивающей главной — **~20 HTTP-запросов/мин** от шести независимых `setInterval` при открытом SignalR-канале:

- `/api/proposals` поллят **четыре** компонента одновременно (`Navigation` 60 с, вложенный в него `BrandHearth` 30 с, `DomovoyDigest` 60 с, `DomovoyRail` 60 с) — всем нужен `list.length`.
- `getMode()` — три компонента (родитель `HomeStateBand` и его ребёнок `DomovoyDigest` запрашивают режим независимо); `activityApi.get()` — три (включая выгрузку 500 строк ради `.length`).
- `/api/capability-devices` тянут 12 страниц; SignalR-хаб подписан только в `Devices` и `ZigbeeDevices`. `Blocks.tsx:111` перезагружает весь список устройств **каждые 5 секунд**; `DeviceRegistry` живёт на поллинге и отстаёт от live-главной до 20 с — пользователь видит два состояния одного дома.
- `Devices.tsx` ↔ `DeviceRegistry.tsx` — ~120 строк построчной копипасты (fetch, zoneName, обработчики команд/зоны/alias/archetype, рендер drawer'а); `DeviceDetailDrawer` при открытии делает до 6 запросов, из них дважды полный список устройств и дважды список блоков — то, что уже есть у родителя.

Прочее: две параллельные системы уведомлений (глобальный `uiStore`+`NotificationContainer` в 4 местах против локальных `useState`+`<Alert>` везде ещё); 10 вызовов `window.confirm` против одного готового MUI-диалога (`DeviceRegistry.tsx:315`); 6 редакторов настроек с дословно одинаковым каркасом `saving/saved/error/touched` (~40 строк × 6 — просится `useSettingsSection`); `actions.save/cancel/delete/edit` продублированы в 8–9 i18n-namespace'ах при живом `common:actions.*`; форматирование дат местами мимо хелперов `i18n/format.ts` (в «Обновлениях» и «Бэкапах» даты отрендерятся локалью браузера, а не UI).

### 4.5. Сборка и словари

- `build/tools/gen-topology.mjs:26-41` не использует общий `spec.mjs` (повторяет `repoRoot`/чтение components.json/manifest вручную) — расхождение формата сломает генератор молча; соседние `plan-build.mjs`/`validate-components.mjs` импортируют корректно.
- Словарь архетипов продублирован в UI и **уже разошёлся**: `api/capabilityDevices.ts:71-74` — 15 значений против 18 в `DeviceArchetypes.All` (нет `tariff`, `person`, `presence`). Паттерн решения уже есть в репозитории: `RolesEndpoints` отдаёт `WellKnownPermissions.All` по HTTP.
- `plugin.json` CommutePlugin декларирует 5 из 10 фактических capabilities; `ContractsPublicApiTests` при отсутствии файла-снимка молча создаёт его и проходит (страж, обнуляющий сам себя в свежей среде).

---

## 5. Читаемость

Общий уровень высокий; «непонятного кода» почти нет — болевые точки это методы-простыни и файлы-универсалы.

### 5.1. Проблемные файлы (по убыванию)

| Строк | Файл | Проблема |
|---:|---|---|
| 852 | `Tools/Domovoy.DeviceEmulator/EmulatorEngine.cs` | 5 обязанностей: MQTT-клиент, реестр+HTTP-фасад, **4 физических симуляции** (thermal/CO2/давление/мощность) со своими моделями, билдеры конфигурации, проекция для WebSocket. Методы короткие — распил по `Simulation/*Simulator.cs` даст ~250 строк ядра. Бонус-дефект: `TickSeconds = 15` с комментарием «must match the loop delay below», а в цикле — литерал `15` (`:590`); локальный `new Random()` при существующем поле `_rng` |
| 823 | `AutomationService/Services/DbGatewayClient.cs` | 43 метода-близнеца (`try { GetFromJsonAsync } catch { LogWarning; return null }`) + ~12 вложенных DTO (строки 700–823), от которых зависят сигнатуры половины сервиса. Просится разбиение по доменам + вынос DTO |
| 788 | `DbGateway/Services/EventInterceptor.cs` | 5 ответственностей: 8 подписок, read-model, event-log+телеметрия, корреляция команда→состояние, **watchdog живости Zigbee-моста** с собственным `Task.Run` — при том что рядом уже есть `DeviceLivenessWatchdog` для нативных устройств |
| 765 | `WebUI/src/pages/Settings.tsx` | Главный монолит фронтенда: **34 `useState`**, 7 `useEffect`, 5 несвязанных доменов + оркестрация 7 вложенных редакторов, JSX ~380 строк подряд, импорты посреди файла (строки 107–110). Разбиение на `LocationEditor`/`CalendarEditor`/`BackupsEditor` по образцу существующих `TariffEditor`/`IntelligenceEditor` → ~200 строк |
| 646 | `AutomationService/Services/Discovery/DiscoveryEngine.cs` | `ScanOnceAsync` — **~280 строк** (79–356): 8 почти идентичных foreach-блоков (patterns/setpoints/ML/scene-configs/scene-schedules/dead-rules/refinements/drifts), каждый повторяет лимит→дедуп→CreateRule→Proposal→счётчик. Просится `IProposalMiner` + цикл по майнерам |
| 341 | `DbGateway/Endpoints/SettingsEndpoints.cs` | Один метод `MapSettingsEndpoints` на **221 строку**: 7 несвязанных доменов настроек, 7 коллекций, 7 DTO, 7 `GetXOrDefault` — grab-bag |
| 266/246 | `ApiGateway/Program.cs` / `AutomationService/Program.cs` | `Main`-простыни; во втором ~15 inline-лямбд эндпоинтов. Просятся `AddXxx()`/`MapXxx()` extension-методы |
| 527/506/502 | WebUI `Blocks.tsx` / `ZigbeeDevices.tsx` / `DeviceDetailDrawer.tsx` | 11–14 `useState`, инлайн-диалоги, у drawer'а — 6 `useEffect` с одинаковым `cancelled`-boilerplate и подавленный eslint exhaustive-deps |
| 156 (метод) | `Updater/Services/UpdateExecutor.cs:115` | `RunAsync`: preflight→backup→topology→pull→recreate→self-replace одной лентой |

### 5.2. Мелочи гигиены

- Смешение стилей инициализации Serilog: `Connectivity` — `builder.Services.ConfigureSerilog()`, остальные шесть сервисов — `builder.Host.ConfigureSerilog()`.
- `tests/Domovoy.IntegrationTests` — 90 файлов в плоском каталоге, unit и infra вперемешку; подпапки по домену улучшили бы навигацию.
- `HomeConfiguration.cs` — 10 public DTO в одном файле; `Payloads.cs:10-13` — висячий `<summary>` без типа.
- WebUI: страница «Активность» осталась `Logs.tsx` + маршрут `/logs` + namespace `logs` + папка `components/logs/` — сбивает при поиске; BOM в 6 файлах локалей (упадёт любой Node-скрипт с `JSON.parse`); нет code-splitting страниц (`React.lazy` — 0 использований, тяжёлые `Blocks`+xyflow+dagre и `Settings`+pigeon-maps в основном бандле); таймер в `Logs.tsx:83`/`Plugins.tsx:64` назван `t` и затеняет функцию перевода; коллизия ключа `newResident` в двух namespace'ах с разным смыслом.

### 5.3. Что хорошо (сохранять как образцы)

`Domovoy.Narrative` (8 файлов ≤276 строк, данные отделены от кода, веса — именованные константы); связка `LoadShedPlanner` (чистый, без I/O) + `LoadManager` (шина/состояние); `DependencyResolver` 3K (чистый, 10 тестов); `contract-guard.sh` и `spec.mjs` (длинное «почему», сообщение об ошибке с рецептом); WebUI: единый axios-клиент без сырых `fetch`, вынесенная чистая логика (`blockGraphModel`, `ruleReadable`, `proposalText`, `homeSummary`) под юнит-тестами, полный паритет ru/en локалей.

---

## 6. Рекомендации (в порядке эффект/трудозатраты)

Решение по каждому пункту — за владельцем; код в рамках этого отчёта не менялся.
Пошаговая инструкция к исполнению (этапы, ветки, точные правки, команды проверки, пометки
совместимости) — [`docs/cleanup_plan_ru.md`](docs/cleanup_plan_ru.md).

**Перед живым прогоном 3K (маленькие правки, большой эффект):**
1. `UpdateExecutor.cs:340`: `/api/backups/run` → `/api/backup/run` (+ пробросить `reason` или убрать параметр) — однострочный фикс тихо отключённой защиты данных.
2. `RabbitMqConfig.UseMqtt` → `false` по умолчанию (или удалить MQTT-ветки из `RabbitMQConnection` — осознанно ими никто не пользуется): убирает мусорные заголовки, подмену routing key и тихое отключение DLQ.
3. Хвосты 3K одним заходом: `UpdateCheckService` читает `CheckEnabled`/`CheckIntervalHours` из БД + пишет `check-result` (или эндпоинт удаляется); уведомление об обновлении — через `NotificationDispatcher`; `domovoy-updater` — в `SystemController.KnownServices`; удалить временный триггер из `release.yml` последним коммитом эпика.
4. CI: `dotnet test Domovoy.sln --filter "Category!=Infra"` вместо адресного проекта — включает 269 строк тестов CommutePlugin.
5. Плагин в конвейер: `src/Plugins/**` в `build/components.json` + в `CONTRACT_PATTERNS` contract-guard + публикация DLL в образ супервизора либо отдельный компонент.

**Чистка (риск нулевой, ~1 000 строк):** разделы 3.1–3.3 целиком — мёртвые файлы, члены, перегрузки, i18n-ключи, npm-зависимости; плюс 5-минутные фиксы: NUL-байты в `useTelemetryBatch.ts`, `typeof(T)` в `Coerce`, `TickSeconds` вместо литерала в эмуляторе. Единственная оговорка — `RuleStatus.Approved` (BSON-порядок, см. 3.2).

**Рефакторинги (по мере планов):**
6. WebUI: разделяемый слой данных для «горячей четвёрки» (proposals/mode/activity/devices) — zustand-стор или воскрешённый `PollingService`; хук `useDeviceCollection()` с SignalR (снимает копипасту Devices↔DeviceRegistry, 5-секундный полл Blocks и двойные запросы drawer'а). Трафик главной — примерно втрое ниже.
7. ApiGateway: единый `GatewayProxy` со стримингом и единой политикой query-string (~500 строк копипасты, латентная потеря query).
8. Разбор монолитов: `Settings.tsx` на секции + `useSettingsSection`; `ScanOnceAsync` на майнеры; `SettingsEndpoints` по доменам; `EventInterceptor` — вынести bridge-watchdog; `DbGatewayClient` по доменам с выносом DTO.
9. Гигиена шины: `DomovoyJson` (единые `JsonSerializerOptions`) в Contracts + явное применение в `RabbitMqConnection`; перенос `IMessageBus` в проект `MessageBus`; общий `ResolveTimeZone` (6 копий) и коэрсия JsonElement (8 мест); эндпоинт словаря архетипов вместо дубля в UI.
10. Наблюдаемость: метки Prometheus по route-template (`UseHttpMetrics()` в ApiGateway), await подписок в `EventRelayService`/`ZigbeeBridgeStateCache`.

---

## 7. Отложенные архитектурные решения (зафиксировано с владельцем, 2026-08-07)

1. **Снятие `UnifiedDeviceService`** — отложено до окончания живого прогона 3K. Содержание: подписать `ApiGateway.EventRelayService` напрямую на `Envelope<DeviceStateReportV1>` (StateExchange), удалить сервис, контейнер из compose, legacy-тип `DeviceStateUpdatedEvent` и exchange `device.events`. Это правка топологии и контрактов шины → по правилам «Контракты и совместимость» ([`docs/architecture/coding_standards_ru.md`](docs/architecture/coding_standards_ru.md)) потребует поднятия `bus.speaks/understands`, `topology.version` в manifest и версий компонентов. Выигрыш: минус контейнер, минус hop в hot-path состояния, минус последний legacy-контракт (и большая часть причин существования `Domovoy.Common.Models`). Зафиксировано также как техдолг-кандидат в roadmap (phase-4).
2. **Консолидация прокси ApiGateway** (п. 7 раздела 6) — вместе с решением о YARP.
3. **Довод шва 3H Ф1** (~24 домена) — уже в плане эпика 3H, отчёт лишь фиксирует текущую глубину (2 файла).
4. **Слой данных WebUI** (п. 6 раздела 6) — отдельным заходом, не смешивая с чисткой.
