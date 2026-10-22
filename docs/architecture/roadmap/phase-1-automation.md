[← Дорожная карта](../roadmap.md)

# Фаза 1 — Автоматизация и платформа данных (СЕЙЧАС)

**Цель фазы:** продукт начинает работать «по сценариям» сам; появляется фундамент данных для ML и SDK для расширений.

> **Статус фазы (на 2026-06-01): функционально закрыта.** 1A ✅ · 1B ✅ · 1C ✅ (супервизор-фундамент) ·
> 1D ✅ · 1E ✅ (визуальный flow-редактор) · 1F ✅ · 1G ✅ · 1H ✅ (E1-фундамент). Все эпики Фазы 1 имеют
> рабочую реализацию; остаются «дальше»-хвосты внутри 1C/1H (ALC, E2-композиты и т.п.).
> **Рантайм против реального RabbitMQ/Mongo не прогонялся.**

### Эпик 1A. AutomationService — детерминированный движок ✅
Отдельный сервис, подписан на шину, держит состояние правил.

> **✅ Реализовано (Epic 1A).** Новый сервис [`Domovoy.AutomationService`](../../../src/Services/Domovoy.AutomationService/):
> модель правила в контракте [`AutomationRule`](../../../src/Common/Domovoy.Contracts/Automations/AutomationRule.cs)
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
- **Модель правила** `trigger → condition → action` в контракте [AutomationRule](../../../src/Common/Domovoy.Contracts/Automations/AutomationRule.cs):
  - триггеры: событие устройства/зоны, изменение состояния, время, **солнечные события** (восход/закат) — для наружного света;
  - условия: состояние устройств/зон, время суток, присутствие, режим дома;
  - действия: команды устройствам, установка уставок, уведомления, задержки/таймеры.
- **Планировщик:** cron-расписания (полив), таймеры, sun-based триггеры.
- **История срабатываний** в [AutoHistory](../../../src/Gateway/Domovoy.DbGateway/Models/AutoHistory.cs).
- **Безопасный пол:** защитные правила, которые нельзя отключить из UI (антизамерзание, CO2 > порога → вентиляция, дым → разблокировать замки).
- **Очередь предложений с апрувом:** статусы правила `Proposed → Approved → Active → Disabled` (заранее — под ML из Фазы 2).
- **DoD:** правило «движение в коридоре после заката → включить свет на 5 мин» создаётся, сохраняется, срабатывает и пишется в историю; защитное правило работает даже при недоступном UI. ✅ (выражается через device-state триггер + sun-условие `dark` + действия command/delay/command; safety floor — из локального файла. Рантайм с RabbitMQ/Mongo не прогонялся.)

### Эпик 1B. Платформа данных для телеметрии ✅
- Использовать **MongoDB time-series collections** (решение принято: единая БД, отдельный TSDB не вводим) рядом с текущим состоянием в Mongo и журналом из P0-5.
- Ретеншн-политики, агрегация (минутные/часовые свёртки), экспорт за период.
- **DoD:** показания климат-датчиков пишутся в Mongo time-series; график «температура зоны за сутки» строится из API.

> **✅ Реализовано (Epic 1B).** Поверх готового `sensor_readings` (P0-5, запись телеметрии уже шла).
> **Ретеншн:** [`TelemetryOptions.RawRetentionDays`](../../../src/Gateway/Domovoy.DbGateway/Config/TelemetryOptions.cs)
> (0 = хранить вечно) → TTL на `sensor_readings` через
> [`TimeSeriesInitializer`](../../../src/Gateway/Domovoy.DbGateway/Services/TimeSeriesInitializer.cs)
> (`ExpireAfter` при создании + `collMod` на старте, чтобы смена политики применялась). **Доменный
> event-log `device_events` сознательно НЕ истекает** — это реплейабельный feature store (P0-5/1F).
> **Агрегация (свёртки):** `GET /api/telemetry/aggregate?bucket=minute|hour|day&agg=avg|min|max` —
> on-the-fly свёртки через Mongo `$dateTrunc`+`$group` (avg/min/max/count на бакет; фильтры
> deviceId/capabilityId/**zoneId**/from/to), в [`HistoryEndpoints`](../../../src/Gateway/Domovoy.DbGateway/Endpoints/HistoryEndpoints.cs).
> Отдельный rollup-store не вводим (homelab): свёртки считаются из сырых семплов в пределах окна ретеншна.
> **Экспорт за период:** `GET /api/telemetry?...&format=csv`. Прокси в ApiGateway `HistoryController`.
> **WebUI:** компонент [`TelemetryChart`](../../../src/UI/WebUI/src/components/charts/TelemetryChart.tsx)
> (recharts area-chart, 24ч/часовые бакеты, scope по device **или** zone) — встроен в детальный drawer
> устройства секцией «Trends · last 24h» для числовых сенсоров. График «температура зоны за сутки»
> строится тем же эндпоинтом/компонентом по `zoneId`. 8 .NET-проектов + `tsc`/lint/26 тестов зелёные.
> **Не проверено вживую** против Mongo (агрегация `$dateTrunc`, TTL `collMod`).

### Эпик 1C. Integration SDK — внепроцессные плагины поверх шины ✅ (супервизор-фундамент)
- Обобщить [IProtocolAdapter](../../../src/Services/Domovoy.Connectivity/Adapters/IProtocolAdapter.cs) из «адаптера протокола» в **«интеграцию»**.
- **Манифест плагина:** id, версия, предоставляемые capability, подписки на события, принимаемые команды, требуемые права, **требуемые ресурсы (CPU/RAM/GPU, интернет)**.
- **Контрактный пакет** = `Domovoy.Contracts` (P0-1): для .NET — NuGet, для Python — pip-пакет со схемами (под будущий ML/голос).
- **Реестр + супервизор:** обнаружение, запуск/остановка/health, версии, изоляция прав. Плагин — отдельный процесс; падение/обновление не влияет на ядро.
- **Resource-aware включение:** супервизор сверяет требования манифеста с мощностями хоста (homelab) и доступностью интернета — тяжёлые/облачные плагины включаются только при наличии ресурсов, иначе система работает на базовом функционале.
- Гибрид: лёгкие доверенные first-party расширения допустимо грузить в процесс через `AssemblyLoadContext` (collectible, выгрузка без рестарта).
- **DoD:** новый адаптер устройства подключается как плагин по манифесту, без перекомпиляции ядра; остановка плагина не роняет остальные сервисы; плагин с невыполнимыми требованиями ресурсов автоматически не активируется.

> **✅ Реализовано (Epic 1C — супервизор-фундамент).** Новый сервис
> [`Domovoy.PluginSupervisor`](../../../src/Services/Domovoy.PluginSupervisor/) (WebApplication, Kestrel :8080,
> метрики :9090). **Манифест** [`PluginManifest`](../../../src/Common/Domovoy.Contracts/Plugins/PluginManifest.cs)
> (id/version/capabilities/subscriptions/commands/permissions + `ResourceRequirements` cpu/ram/gpu/internet +
> autostart/enabled), обнаруживается как `plugin.json` в подпапках plugins-root. **Resource-aware гейтинг:**
> [`HostResources`](../../../src/Services/Domovoy.PluginSupervisor/Resources/HostResources.cs) (cpu/ram из рантайма,
> gpu/internet из конфига) × требования манифеста → плагин с невыполнимыми ресурсами помечается `Blocked` и не
> запускается (DoD #3). **Супервизор** [`PluginSupervisor`](../../../src/Services/Domovoy.PluginSupervisor/Plugins/PluginSupervisor.cs):
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
> **Actuation:** [`ControlBlock.Outputs`](../../../src/Common/Domovoy.Contracts/Blocks/ControlBlock.cs) —
> выход блока привязывается к реальному device+capability; [`BlockRuntime`](../../../src/Services/Domovoy.AutomationService/Blocks/BlockRuntime.cs)
> публикует `DeviceCommandV1` (`source=block:{id}`) при изменении значения (dedup) → P0-5 атрибутирует,
> адаптер исполняет, state возвращается на шину (замыкает «команда→исполнение→подтверждение»). **Доменные
> контуры** ([`BuiltInBlocks.cs`](../../../src/Services/Domovoy.AutomationService/Blocks/BuiltInBlocks.cs)):
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
- **Ориентир UX — Homey Advanced Flow**, а НЕ дашборды Lovelace HA: цель — простой и наглядный редактор правил при тезисе «меньше UI», а не богатство дашбордов (см. [`positioning_ru.md`](../positioning_ru.md), «чем мы НЕ являемся»).
- **DoD:** пользователь создаёт/редактирует правило и задаёт уставки зон из UI; видит реалтайм-состояние.

> **✅ Реализовано (Epic 1E).** **Визуальный flow-редактор** на **React Flow** (ориентир Homey Advanced Flow)
> — страница [`Flow.tsx`](../../../src/UI/WebUI/src/pages/Flow.tsx): правило рисуется графом **When (триггер) →
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
> [`BlockGraph`](../../../src/UI/WebUI/src/components/blocks/BlockGraph.tsx): блоки = узлы, input/output-биндинги =
> рёбра к устройствам-источникам/актуаторам; **композиция блок→блок** видна там, где вход одного блока привязан
> к виртуальному устройству другого (blackboard 1H). Переключатель «Список ↔ Граф» на `/blocks`. Очередь апрува
> на предложения — уже в 2C (`/proposals`).

> **✅ Хвост — drag-connect-save редактор блоков (2026-07-08, ветка `epic-1h-e2-composites`).** `BlockGraph`
> из read-only-визуализации стал **интерактивным**: кастомные узлы блок/устройство с хэндлом на каждый
> порт/capability (id хэндла кодирует порт/cap → нарисованная связь однозначно маппится в `PortBinding`, без
> пикера), перетаскивание сохраняет `ControlBlock.Layout {x,y}` (новое поле; DbGateway `PUT` его сохраняет при
> не-layout-правке), двойной клик по ребру снимает привязку, Save шлёт только «грязные» блоки. Вся логика
> связывания — в чистой [`blockGraphModel.ts`](../../../src/UI/WebUI/src/components/blocks/blockGraphModel.ts)
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
> AGPL+commercial): чистая [`computeAutoLayout`](../../../src/UI/WebUI/src/components/blocks/blockGraphLayout.ts)
> (LR-раскладка, центр dagre→top-left RF, unit-тест), кнопка «Auto-arrange» кладёт позиции блоков в сохраняемый
> `Layout` (persist + dirty), позиции устройств эфемерны; `fitView` после раскладки.
> **(3) undo/redo** чистым snapshot-редьюсером [`history.ts`](../../../src/UI/WebUI/src/components/blocks/history.ts)
> (past/present/future, cap 50, unit-тест): connect/unbind/drag-settle/auto-layout коммитятся как шаги истории;
> кнопки Undo/Redo + `Ctrl+Z`/`Ctrl+Shift+Z`/`Ctrl+Y`; форс-ресинк узлов канвы при undo/redo layout-only шага
> (сигнатура ре-синка по-прежнему игнорирует позицию → 5-с device-poll не перестраивает канву). i18n-строки
> графа (ru/en). 10 новых vitest (история 6 + layout 4) → **82 WebUI-теста + `tsc` + `vite build` зелёные**;
> lint новых файлов чист. **UI-клик-прогон вживую ещё не делали.** Blockly/Node-RED как альтернативная парадигма
> в исследовании не оценены (пробел покрытия) — при желании отдельное мини-исследование.

### Эпик 1F. Объяснимость и реплей/симуляция автоматизаций ✅

> Это **ров №2** ([`positioning_ru.md`](../positioning_ru.md)) и главное отличие от всех конкурентов.
> Почти бесплатно вытекает из event-log (P0-5) — но только если его схема собрана полно. Это мост
> доверия к ML из Фазы 2: предложение модели валидируется против реальной истории до активации.

- **Объяснимость действий:** каждое действие системы трассируется к правилу/предложению + событию-триггеру («почему включился свет»). Опирается на `triggerSource`/`ruleId`/`decisionId` из P0-5 и на [AutoHistory](../../../src/Gateway/Domovoy.DbGateway/Models/AutoHistory.cs).
- **Реплей/симуляция:** проиграть исторический поток событий через движок правил (1A) → «как сработало бы это правило на прошлой неделе» БЕЗ исполнения реальных команд (dry-run против истории).
- **Стадийный выкат правила:** `Proposed → Shadow (логирует, что сделал бы) → Bounded-Active (в безопасных границах) → Full` — поверх статусов правил из 1A.
- **DoD:** для любого совершённого действия UI показывает причину (правило + триггер); новое правило можно прогнать на истории за период и увидеть, когда оно сработало бы, до его активации.

> **✅ Реализовано (Epic 1F).** **Реплей/симуляция:** [`ReplayService`](../../../src/Services/Domovoy.AutomationService/Services/ReplayService.cs)
> прогоняет правило-кандидат по историческим device-дельтам из event-log (P0-5): реконструирует состояние
> устройств по ходу и оценивает device-state триггеры + условия в каждой точке тем же `RuleEvaluator`, что и
> живой движок (симуляция == прод-семантика), **без публикации команд**. Условия считаются по локальному
> времени события и `mode`, штампованному на записи (1G). AutomationService переведён на WebApplication
> (Kestrel :8080, метрики остаются на :9090) и отдаёт `POST /api/replay` (правило + окно дней → когда бы
> сработало); прокси `ReplayController` в ApiGateway (+ httpclient `automation-service`). Время/sun-триггеры
> в реплее помечаются как не симулируемые. **Объяснимость:** в детальном drawer устройства изменения с
> `triggerSource=rule` показывают чип «via <имя правила>» (атрибуция через `correlationId`→правило). Полный
> trace срабатываний — уже в `auto_history` (страница `/automations` → History, `triggerSummary`).
> **Стадийный выкат:** добавлен статус [`RuleStatus.Shadow`](../../../src/Common/Domovoy.Contracts/Automations/AutomationRule.cs) —
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
> ([`WellKnownModes`](../../../src/Common/Domovoy.Contracts/Home/HomeMode.cs): `Home/Away/Night/Vacation`,
> строки-расширяемо, а не enum) + событие [`HomeModeChangedV1`](../../../src/Common/Domovoy.Contracts/Messaging/Payloads.cs).
> **DbGateway — авторитет персиста:** single-doc коллекция `home_state`
> ([`HomeState`](../../../src/Gateway/Domovoy.DbGateway/Models/HomeState.cs)) + эндпоинты
> [`ModeEndpoints`](../../../src/Gateway/Domovoy.DbGateway/Endpoints/ModeEndpoints.cs) (`GET /api/mode`,
> `GET /api/mode/options`, `PUT /api/mode` — апсертит и публикует `HomeModeChangedV1` на шину при реальной
> смене). Прокси [`ModeController`](../../../src/Gateway/Domovoy.ApiGateway/Controllers/ModeController.cs).
> **event-log как фича (P0-5):** [`EventInterceptor`](../../../src/Gateway/Domovoy.DbGateway/Services/EventInterceptor.cs)
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

> **✅ Реализовано (Epic 1H — E1-фундамент).** **Block-SDK** ([`BlockSdk.cs`](../../../src/Services/Domovoy.AutomationService/Blocks/BlockSdk.cs)):
> `IBlock` (`Tick`), `IBlockType` (TypeId + схема портов/параметров/выходов + фабрика), `IBlockContext`
> (`Read`/`ReadNumber`/`Param`/`Commanded`/`Emit`/`GetState`/`SetState`/`Now`/`Log`). **Каталог** built-in
> типов ([`BlockCatalog`](../../../src/Services/Domovoy.AutomationService/Blocks/BlockCatalog.cs)) + **рантайм**
> ([`BlockRuntime`](../../../src/Services/Domovoy.AutomationService/Blocks/BlockRuntime.cs)) — отдельный быстрый
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
> рестарта): [`CompositeDefinition`](../../../src/Services/Domovoy.AutomationService/Blocks/Composite/CompositeDefinition.cs)
> (узлы-примитивы + внутренние биндинги + внешние порты), [`CompositeBlockType`/`CompositeBlock`](../../../src/Services/Domovoy.AutomationService/Blocks/Composite/CompositeBlock.cs)
> — first-class `IBlockType`, тикает дочерние примитивы по внутреннему blackboard (`InternalContext` мостит
> внешние порты к родительскому контексту, состояние namespaced по узлу, внутр. сигналы живут между тиками).
> **DSL (авторинг B)** [`BlockDsl`](../../../src/Services/Domovoy.AutomationService/Blocks/Composite/BlockDsl.cs):
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
