# План чистки и багфиксов по итогам аудита 2026-08-07

> Рабочая инструкция для исполнения в отдельной сессии. Основание —
> [`PROJECT_ANALYSIS_REPORT.md`](../PROJECT_ANALYSIS_REPORT.md) v3.0 (2026-08-07): там доказательства
> и контекст по каждому пункту, здесь — что именно делать, в каком порядке и как проверить.
>
> **Этапы независимы и упорядочены по «риск ↔ ценность».** Этап 1 стоит закрыть до живого прогона
> Эпика 3K, остальные — по решению владельца. Каждый этап = отдельная ветка/коммит, чтобы откат был
> дешёвым.

---

## Общие правила исполнения

**Перед началом:** прочитать [`memory-bank/README.md`](../memory-bank/README.md) →
[`activeContext.md`](../memory-bank/activeContext.md), затем нужный раздел отчёта.

**Совместимость.** Любая правка контрактов, HTTP-границ между сервисами или `docker-compose.yml`
требует поднятия версий в [`build/components.json`](../build/components.json) и/или
[`build/topology/manifest.json`](../build/topology/manifest.json) — полная таблица случаев в разделе
«Контракты и совместимость» [`coding_standards_ru.md`](architecture/coding_standards_ru.md). Если
изменение обратно совместимо — сказать об этом явно и добавить в тело коммита строку `Compat: none`.
Ожидаемая пометка указана в каждом этапе; проверять по таблице, а не доверять ей вслепую.

**Проверка (одинаковая для всех этапов):**

```bash
dotnet build Domovoy.sln -c Release
dotnet test tests/Domovoy.IntegrationTests/Domovoy.IntegrationTests.csproj -c Release --filter "Category!=Infra"
cd src/UI/WebUI && npm test && npm run build
```

Инфра-тесты (нужен Docker): `--filter "Category=OfflineSmoke"`. Базовые числа на входе:
.NET 477 тестов, WebUI 237, решение собирается.

**После каждого этапа** — обновить [`memory-bank/activeContext.md`](../memory-bank/activeContext.md)
и, если менялся статус эпика, соответствующий `docs/architecture/roadmap/phase-*.md`.

---

## Этап 1. Дефекты Эпика 3K — до живого прогона

**Ветка:** текущая `epic-3k-delivery` (эпик не смёржен, правки его же кода).
**Объём:** ~1 час. **Риск:** низкий. **Compat:** см. по пунктам.

### 1.1. Бэкап перед обновлением (главное)

- `src/Services/Domovoy.Updater/Services/UpdateExecutor.cs:340` — маршрут `/api/backups/run` →
  `/api/backup/run`. Сейчас 404 глотается в `RequestBackupAsync` с warning, и при
  `BackupBeforeUpdate = true` (дефолт) страховочный бэкап **не создаётся**.
- Параметр `?reason=pre-update` сейчас игнорируется: `src/Gateway/Domovoy.DbGateway/Endpoints/BackupEndpoints.cs:53`
  хардкодит `CreateBackupAsync("manual", ct)`. `reason` попадает в манифест (`BackupService.cs:94`) —
  осмысленно принять его: `MapPost("/run", (string? reason, …) => … CreateBackupAsync(reason ?? "manual", ct))`.
- **Тест:** добавить проверку, что `UpdateExecutor` бьёт в существующий маршрут (иначе дефект такого
  класса вернётся молча).
- **Compat:** приём необязательного query-параметра обратно совместим → `Compat: none`.

### 1.2. Настройки проверки обновлений читаются из БД

- `src/Services/Domovoy.Updater/Services/UpdateCheckService.cs:48,71` — `CheckEnabled` и
  `CheckIntervalHours` берутся из env (`UpdaterOptions`); из БД `UpdateCatalog.GetChannelAsync`
  берёт только `Channel`. Тумблер и интервал на `/settings` ни на что не влияют.
- Сделать: читать те же поля из `UpdateSettings` (env — дефолт при отсутствии записи), перечитывать
  их на каждой итерации цикла, а не один раз на старте (иначе изменение из UI ждёт рестарта).
- **Compat:** Updater становится потребителем `/api/update-settings` → проверить `requires` у
  компонента `updater` в `build/components.json`.

### 1.3. Отметка о проверке

- `POST /api/update-settings/check-result` (`UpdateSettingsEndpoints.cs:62-68`) + `IUpdateStore.RecordCheckAsync`
  никем не вызываются → `lastCheckResult`/`lastCheckAt` в UI вечно пусты (`WebUI/src/api/updates.ts:23`).
- Решение на выбор: **либо** звать эндпоинт из `CheckOnceAsync` (по успеху и по ошибке), **либо**
  удалить эндпоинт, метод стора и поля модели вместе с их отображением в UI. Полумеры оставлять нельзя.

### 1.4. Уведомление об обновлении — через дисциплину 3F

- `UpdateCheckService.cs:117` публикует `NotificationRaisedV1` прямо в
  `BusTopology.EventsExchange/NotificationRaisedKey` — то есть **на выход** `SignalRChannel`, минуя
  `NotificationDispatcher`/`NotificationPolicy` (mute-каналы, rate-limit, dedup, маршрутизация по
  категориям, safety-floor). Уведомление физически не может уйти в ntfy/Telegram/webhook, а XML-doc
  класса (строки 19–23) утверждает обратное.
- Сделать: провести через диспетчер (Updater — отдельный процесс, значит нужен тот же путь, которым
  пользуются прочие внешние источники), либо — если сознательно оставляем прямую публикацию —
  переписать XML-doc и явно обосновать исключение. Заодно `_lastAnnounced` (in-memory fingerprint,
  сбрасывается при рестарте) заменяется штатным dedup политики.

### 1.5. Мелочи того же эпика

- `src/Gateway/Domovoy.ApiGateway/Controllers/SystemController.cs:36-44` — добавить
  `new("domovoy-updater", "service")` в `KnownServices`: служба зовёт `AddSystemControl("domovoy-updater")`
  и умеет самоперезапуск, но из UI не рестартуется.
- `.github/workflows/release.yml:19-21` — удалить временный триггер `epic-3k-delivery`
  **последним коммитом эпика** (в файле стоит самоописанная пометка «УДАЛИТЬ»).

---

## Этап 2. Гигиена шины: `UseMqtt`

**Ветка:** `fix/bus-mqtt-default`. **Объём:** ~1–2 часа. **Риск:** средний — трогает объявление
очередей у всех сервисов, требует внимания при первом развёртывании.

- `src/Common/Domovoy.MessageBus/RabbitMqConfig.cs:45` — `UseMqtt = true` по умолчанию; `false` задан
  только в appsettings AutomationService и UnifiedDeviceService, в compose переменной
  `RABBITMQ__USEMQTT` нет вовсе. Итог: 6 из 8 сервисов гоняют MQTT-ветки
  (`RabbitMQConnection.cs:153-160, 190-196, 201-204`), а из-за аргумента `mqtt-subscription-qos` в
  объявлении очереди срабатывает fallback «declaring without dead-lettering» (274–284) — **DLQ тихо
  отключён**.
- Сделать: `UseMqtt = false` по умолчанию. Лучше — удалить MQTT-ветки целиком: осознанно ими не
  пользуется никто, MQTT-брокер устройств — отдельный контур (`AdapterManager.cs:95` читает
  `MqttPort`, это единственная живая ссылка, и она из другого домена — заодно развести настройки).
- ⚠️ **Операционный момент.** На уже развёрнутом стенде очереди объявлены со старыми аргументами:
  после правки объявление без них даст `PRECONDITION_FAILED 406` и снова уронит в fallback без DLQ.
  Поэтому в план развёртывания входит **удаление старых очередей** (или их переименование в коде)
  при первом обновлении. Проверять по факту: у каждой очереди `domovoy.*` должен появиться
  `x-dead-letter-exchange: domovoy.dlx` и парная `<queue>.dlq`.
- Попутно: `RabbitMqConfig.MaxReconnectAttempts`/`ReconnectInterval`/`AutoCreateStructures` и
  `RabbitMqConnection.IsMqttEnabled` — мёртвые (реальные ретраи зашиты константами `:30,116`);
  удалить, чтобы не выглядели как настройки.
- **Compat:** поведение транспорта, не контракт → `Compat: none`, но версии затронутых компонентов
  поднимаются как обычно (правка `src/Common/**` = shared path → пересобираются все .NET-компоненты).

---

## Этап 3. Конвейер: плагин и CI

**Ветка:** `fix/delivery-pipeline`. **Объём:** ~1–2 часа. **Риск:** низкий (сборка/CI).

- `.github/workflows/ci.yml:61,96` — оба шага `dotnet test` адресуют только
  `Domovoy.IntegrationTests.csproj`, из-за чего `tests/Domovoy.CommutePlugin.Tests` (269 строк,
  единственные тесты `PluginSettingsService.Coerce`) **никогда не выполняются**. Заменить на
  `dotnet test Domovoy.sln -c Release --no-build --filter "Category!=Infra"`.
- `build/components.json` — `src/Plugins/**` (и `src/Tools/**`) не входят ни в один компонент и ни в
  `sharedPaths`: правка плагина даёт пустую build-матрицу, DLL не попадают ни в один образ (доставка
  только через host-volume `./plugins`), `build/tools/contract-guard.sh:36` изменений плагина не
  видит. Решить, как плагин доставляется — отдельный компонент-образ или публикация в образ
  супервизора, — и внести в спецификацию + `CONTRACT_PATTERNS`.
- `build/tools/gen-topology.mjs:26-41` — использовать общий `spec.mjs` (`repoRoot`, `loadSpec`,
  `loadTopologyManifest`) вместо ручных копий, как это уже делают `plan-build.mjs` и
  `validate-components.mjs`.
- `tests/Domovoy.IntegrationTests/ContractsPublicApiTests.cs:44-50` — при отсутствии файла снимка тест
  молча создаёт его и проходит: страж обнуляет себя в свежей среде. Падать, если снимка нет и
  `UPDATE_APPROVALS != 1`.
- **Compat:** если появляется новый компонент поставки — обязательна запись в `components.json`
  (не `Compat: none`).

---

## Этап 4. Чистка мёртвого кода

**Ветка:** `chore/dead-code`. **Объём:** ~2–3 часа. **Риск:** низкий, но делить на 3 коммита —
по зонам, чтобы дифф читался. Полная инвентаризация с доказательствами — раздел 3 отчёта.

**4.1. WebUI** (~350 строк): `src/api/polling.ts` целиком; кластер старых логов `api/logs.ts` +
`types/log.ts` + `types/api.ts`; барели `store/index.ts` и `components/layout/index.ts`;
`CardSkeleton`; loading-срез `uiStore`; `deviceZoneName`, `LoadSheddingTier`, `NotificationCategory`;
11 мёртвых i18n-ключей (список в отчёте, 3.3) — в обоих языках; зависимости `date-fns`,
`react-hook-form`, `fast-check` из `package.json` (**`@emotion/*` не трогать** — peer-зависимости MUI).
Проверка: `npm run build` + `npm test` + `npm run lint`.

**4.2. Backend:** `DbGateway/Models/UserAccess.cs`; `DbGateway/Config/RabbitMQConfig.cs`
(дубликат-ловушка, отличается от рабочего класса регистром одной буквы); `ApiGateway/Controllers/StatusController.cs`
целиком (анонимно отдаёт версию и внутренние URL, 0 потребителей); `RequestLoggingMiddlewareExtensions`;
мёртвые перегрузки `MlTrainingService.BacktestAsync(int, ct)` и `LoadShedPlanner.PlanRestore(...)` без
scope (вторую — вместе с переводом теста на живую перегрузку). Отдельно решить по
`Ml/Templates/FeatureLocality.cs`: политика не применяется ни тренером, ни шаблонами — либо подключить,
либо явно пометить «Фаза 4, не подключено» (сейчас документированный инвариант не соблюдается).

**4.3. Контракты и библиотеки:** ~65 % `CapabilityState` (аксессоры и ярлыки, живут только `new`,
`Set`, `Values`); `Capability.IsReadable` + ключ `Readable`; фабрики `WellKnownCapabilities.Co2/Valve/
Illuminance/Energy/Color`; `Transition.Reached`, `SunEvent.Sunset`, `VariableType.String/DateTime/List`;
`RabbitMqConfig`-свойства (см. этап 2); `LanguagePack.AvailableLocales`; `CommuteOptions.Provider/
TomTomApiKey` + актуализация `src/Plugins/Domovoy.CommutePlugin/README.md:119-126`; PackageReference
`Microsoft.Extensions.Http` из `Domovoy.Common.csproj:10`.

⚠️ **Две оговорки:**
- `RuleStatus.Approved` **не удалять** механически: enum сериализуется в BSON по порядковому номеру,
  удаление среднего члена молча переинтерпретирует существующие записи `automations`. Либо
  `[BsonRepresentation(BsonType.String)]` + миграция, либо оставить как зарезервированный слот с
  комментарием.
- Архетипы `person`/`presence` объявлены, но классификатор их не присваивает
  (`DeviceClassifier.cs:47-60` уводит устройства присутствия в `motion`) — это **не мёртвый код,
  а пробел 3D**: чинить классификатор, а не удалять значения.

**Compat:** правка публичной поверхности `Domovoy.Contracts` → переутвердить снимок
(`UPDATE_APPROVALS=1 dotnet test --filter ContractsPublicApi`) и разобрать по таблице совместимости:
wire-формат payload'ов не меняется, но `contract-guard` сработает — либо поднять `bus.speaks/understands`,
либо обосновать `Compat: none`.

---

## Этап 5. Точечные корректности

**Ветка:** `fix/small-defects`. **Объём:** ~2 часа. **Риск:** низкий.

- `src/Common/Domovoy.MessageBus/PluginSettingsService.cs:150-170` — дискриминатор
  `default(T) switch { string => … }` недостижим для `string` (`default(T)` = `null`). Заменить на
  `typeof(T)`; тест уже есть в `Domovoy.CommutePlugin.Tests` — но он поедет только после этапа 3.
- `src/Plugins/Domovoy.CommutePlugin/Services/CommutePlanner.cs:382-403` — гонка на `_wake`: CTS
  утилизируется в `finally`, поле продолжает на него указывать, `Cancel()` из обработчиков шины и
  настроек бросает `ObjectDisposedException` → сообщение уходит в DLQ. Обнулять поле под замком либо
  использовать один долгоживущий CTS с пересозданием.
- `ApiGateway/Middleware/RequestCounterMiddleware.cs` + `RouteCounterMiddleware.cs` — метки метрик из
  сырых путей с GUID устройств и именами файлов (бомба кардинальности Prometheus); перейти на
  route-template через `app.UseHttpMetrics()`, как уже сделано в DbGateway (`Program.cs:129`), и убрать
  дублирование смысла между двумя middleware.
- `ApiGateway/Services/EventRelayService.cs:38`, `ZigbeeBridgeStateCache.cs:110-122` — подписки без
  `await` и без токена остановки (ошибки проглатываются, отписки нет); образец рядом —
  `NotificationRelayService.cs:35`. Там же `Updater/Endpoints/UpdateEndpoints.cs:97,113` —
  `Task.Run` без наблюдаемости.
- `WebUI/src/components/charts/useTelemetryBatch.ts:37,68,80,82` — литеральные NUL-байты в
  шаблонных ключах: файл детектится как бинарный и выпадает из поиска/diff. Заменить на `'\0'` или `'|'`.
- `WebUI/src/components/common/DomovoyDigest.tsx:32-34` — тянет 500 строк активности раз в минуту
  ради `.length`; запрашивать счётчик, а не выгрузку (или дождаться этапа 6).
- `WebUI/src/pages/Devices.tsx:150`, `ZigbeeDevices.tsx:259` — функция перевода `t` в зависимостях
  эффекта соединения рвёт и переустанавливает SignalR-хаб при смене языка; вынести строку ошибки.
- `Tools/Domovoy.DeviceEmulator/EmulatorEngine.cs:590` — литерал `15` вместо `TickSeconds`, при том
  что комментарий у константы требует их совпадения; там же убрать локальный `new Random()` при
  существующем поле `_rng`.
- `ApiGateway/appsettings.json` + `Program.cs:81` — дефолтный JWT-секрет в двух местах; убрать
  fallback, требовать явной настройки при `Enabled: true`.
- Дрейф комментариев (раздел 3.1 отчёта): `CapabilityDeviceManager.cs:24-26` ссылается на удалённый
  `UnifiedDeviceManager`, `SignalRChannel.cs:15` — не на тот хаб, `SettingsEndpoints.cs:17` и заголовок
  `EventInterceptor.cs` описывают устаревшую картину.

---

## Этап 6. Рефакторинги (каждый — отдельно, по решению владельца)

Порядок по отношению «эффект / риск»; ни один не обязателен для боевого запуска.

1. **Слой данных WebUI** (`feat/webui-data-layer`). Разделяемый кэш для «горячей четвёрки»
   (`proposals`, `mode`, `activity`, `capability-devices`) — zustand-стор или воскрешённый
   `PollingService` из `api/polling.ts`. Снимает 4-кратный полл предложений, 3-кратный режима и
   активности, 5-секундный полл всего дома в `Blocks.tsx:111` и расхождение «главная live, реестр
   отстаёт до 20 с». Трафик простаивающей главной падает примерно втрое (~20 запросов/мин → ~7).
   Следом — хук `useDeviceCollection()` с подпиской на SignalR: снимает ~120 строк копипасты
   `Devices.tsx` ↔ `DeviceRegistry.tsx` и двойные запросы в `DeviceDetailDrawer`.
   **Учесть:** этот этап делает ненужным пункт 4.1 в части `polling.ts` — решить до чистки, удаляем
   модуль или используем.
2. **Единый прокси ApiGateway** (`refactor/gateway-proxy`). Один хелпер/middleware со стримингом тела
   и единой политикой query-string вместо 25 приватных `Forward` в 23 контроллерах (~500 строк).
   Чинит латентную потерю query-string у 10 контроллеров и буферизацию больших ответов в память.
   Альтернатива — YARP (таблица маршрутов вместо контроллеров).
3. **Разбор монолитов.** `pages/Settings.tsx` (765 строк, 34 `useState`) → `LocationEditor`/
   `CalendarEditor`/`BackupsEditor` по образцу существующих `TariffEditor`/`IntelligenceEditor`
   + общий хук `useSettingsSection(load, save)` (снимает одинаковый каркас в 6 редакторах);
   `DiscoveryEngine.ScanOnceAsync` (~280 строк, 8 одинаковых блоков) → `IProposalMiner` + цикл;
   `SettingsEndpoints.MapSettingsEndpoints` (221 строка, 7 доменов) → по доменам;
   `EventInterceptor` — вынести watchdog Zigbee-моста (рядом уже есть `DeviceLivenessWatchdog`);
   `DbGatewayClient` (823 строки, 43 метода) → по доменам с выносом DTO;
   `EmulatorEngine` (852) → `Simulation/*Simulator.cs`.
4. **Единая сериализация и словари.** `DomovoyJson` с общими `JsonSerializerOptions` в
   `Domovoy.Contracts` + явное применение в `RabbitMqConnection.PublishAsync/SubscribeAsync` (сейчас
   payload едет PascalCase при lower-case конверте, wire-формат не зафиксирован ничем); перенос
   `IMessageBus` в проект `Domovoy.MessageBus` (разрывает зависимость `MessageBus → Common` и снимает
   Serilog с плагинов); общий `ResolveTimeZone` (6 копий) и коэрсия `JsonElement` (8 мест); эндпоинт
   словаря архетипов вместо ручного дубля в `WebUI/src/api/capabilityDevices.ts:71-74`, который уже
   отстал на 3 значения (образец — `RolesEndpoints` отдаёт `WellKnownPermissions.All`).
5. **Единообразие UI-механик.** Локальные `<Alert>`-уведомления → существующий
   `uiStore` + `NotificationContainer`; 10 вызовов `window.confirm` → переиспользуемый `ConfirmDialog`
   (шаблон готов в `DeviceRegistry.tsx:315`); консолидация `actions.*` в `common.json` (~40 дублей
   перевода); code-splitting страниц (`React.lazy` не используется, тяжёлые `Blocks`+xyflow и
   `Settings`+pigeon-maps сидят в основном бандле).

---

## Отложено (решение владельца, 2026-08-07)

**Снятие `UnifiedDeviceService`** — ✅ ВЫПОЛНЕНО 2026-08-08 по решению владельца, не дожидаясь живого
прогона 3K (изначально откладывалось до него). Итог и объявленная совместимость — раздел
«Техдолг-кандидаты» в [`roadmap/phase-4-domain-extensions.md`](architecture/roadmap/phase-4-domain-extensions.md).

Там же по смыслу: **довод шва 3H Ф1** (~24 домена DbGateway за store-интерфейсы; сейчас переведены
2 файла из ~25, `EnergyEndpoints` — гибрид) — это уже отдельный пункт плана Эпика 3H, здесь не
дублируется.
