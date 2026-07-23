[← Дорожная карта](../roadmap.md)

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
> слой авторинга/объяснений. См. [`positioning_ru.md`](../positioning_ru.md). Локальный голос —
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
> - ✅ **Интеграционный data-path тест** против **реальных RabbitMQ + Mongo** ([`tests/Domovoy.IntegrationTests`](../../../tests/Domovoy.IntegrationTests/),
>   Testcontainers): discovery+state по шине → `EventInterceptor` → `capability_devices`/`device_events`/`sensor_readings`;
>   запись `mode_change` (1G); round-trip BSON новых моделей (`ControlBlock`/`HomeState`). Закрывает P0-4
>   (адаптер→шина→Mongo) и риск Mongo-сериализации. Тесты используют только локальные контейнеры (offline-инвариант).
> - ✅ **offline-smoke в CI (2026-07-06):** GitHub Actions [`ci.yml`](../../../.github/workflows/ci.yml) —
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

> **✅ Реализовано (Epic 2A — v1).** Контракт [`MlModel`](../../../src/Common/Domovoy.Contracts/Ml/MlModel.cs)
> + **реестр в DbGateway**: [`MlEndpoints`](../../../src/Gateway/Domovoy.DbGateway/Endpoints/MlEndpoints.cs) —
> `ml_models` (метаданные + артефакт inline), `GET /api/ml/models[/latest]`, `GET …/{id}/artifact`,
> `POST /api/ml/models`. **AutomationService** (без Mongo, всё по HTTP): [`MlTrainer`](../../../src/Services/Domovoy.AutomationService/Ml/MlTrainer.cs)
> (ML.NET SDCA-регрессия время→значение = «обученное расписание»), [`MlModelService`](../../../src/Services/Domovoy.AutomationService/Ml/MlModelService.cs)
> (грузит/кэширует последнюю модель, синхронный `TryPredict` для тика блока), [`MlTrainingService`](../../../src/Services/Domovoy.AutomationService/Ml/MlTrainingService.cs)
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
> ([`MlThermostatBlock.cs`](../../../src/Services/Domovoy.AutomationService/Ml/MlThermostatBlock.cs), E1-тип в
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
> **✅ Хвост — фичи режима (2026-07-06):** **серверный context-join** [`ContextFeatureJoin`](../../../src/Services/Domovoy.AutomationService/Ml/Templates/ContextFeatureJoin.cs)
> (as-of join режима из `mode_change`-лога 1G на каждую тренировочную строку) + **context-шаблон**
> [`ContextScheduleRegressionTemplate`](../../../src/Services/Domovoy.AutomationService/Ml/Templates/ContextScheduleRegressionTemplate.cs)
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
> [`Proposal`](../../../src/Common/Domovoy.Contracts/Proposals/Proposal.cs) (дискриминатор `Kind`
> = `Rule`/`BlockPromotion`/`ModelSelection`; `Status` Proposed→Approved/Rejected; `DecisionId` штампуется при
> апруве) → коллекция **`proposals` в DbGateway**. **Слой 2:** [`ProposalsEndpoints`](../../../src/Gateway/Domovoy.DbGateway/Endpoints/ProposalsEndpoints.cs)
> (`GET ?status=&kind=`, `POST`, `POST /{id}/approve`, `/{id}/reject`); апрув применяет side-effect по типу
> ([`ProposalApplication`](../../../src/Gateway/Domovoy.DbGateway/Endpoints/ProposalsEndpoints.cs) — вынесен для
> тестируемости): **rule** → правило `Active`, **block_promotion** → патч `Params["stage"]`, **model_selection**
> → патч `Params["model_version"]`; мутация цели + перевод предложения в `Approved` в одной БД (новый сервис не
> вводили), reject цель не трогает. **Слой 3:** прокси [`ProposalsController`](../../../src/Gateway/Domovoy.ApiGateway/Controllers/ProposalsController.cs)
> (очередь→db-gateway, suggest→automation-service). **Слой 4 (выбор модели на инстанс):** числовой param
> `model_version` (0 = latest) пробрасывается через governor-хуки (`MlGovernorBase`/setpoint/toggle/selector)
> в предиктор; [`MlModelService`](../../../src/Services/Domovoy.AutomationService/Ml/MlModelService.cs) резолвит
> pinned-версию (lazy-load pending-пина в фоновом refresh; до загрузки — фолбэк на latest scope), снимает
> блокер 2A/2B без нарушения числового инварианта `Params`. **Слой 4.5 (ML→rule предложитель):**
> [`RuleSuggester`](../../../src/Services/Domovoy.AutomationService/Services/RuleSuggester.cs) — BackgroundService
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

> **✅ Реализовано (Epic 2D — v1, эвристики).** Открытый словарь [`DeviceArchetypes`](../../../src/Common/Domovoy.Contracts/Devices/DeviceArchetypes.cs)
> (light/switch/thermostat/climate_sensor/motion/contact/lock/valve/energy_meter/sensor/control_block/unknown).
> Эвристический [`DeviceClassifier`](../../../src/Gateway/Domovoy.DbGateway/Services/DeviceClassifier.cs) (по набору
> capability + adapterSource + model; **явная модель/native-тип приоритетны**; устойчив к `JsonElement` writable
> после транспорта по шине). На discovery `EventInterceptor` пишет `AutoArchetype` (пересчёт на каждый ре-анонс),
> ручной override — `Archetype` (не затирается). `PUT /api/capability-devices/{id}/archetype` (null = вернуть авто)
> + прокси. WebUI: эффективный архетип (`override ?? auto`) задаёт категорию/иконку (откат к старой эвристике при
> unknown), чип типа + селектор override в drawer. **Тесты:** 12 юнит (классификатор) + интеграционный assert на
> discovery против реального Mongo (поймал баг writable-over-bus). 10 .NET-проектов + WebUI `tsc`/lint/26 тестов
> зелёные.
> **✅ Хвост (2026-07-06):** **ML.NET-классификатор** [`ArchetypeClassifier`](../../../src/Services/Domovoy.AutomationService/Ml/ArchetypeClassifier.cs)
> (мультикласс SDCA maximum-entropy над multi-hot сигнатурой capability + флаг writable-`on_off`) — обучается на
> популяции устройств (лейбл = эффективный архетип, override > эвристика) и обобщает на невиданные сигнатуры.
> **Advisory-раннер** [`ArchetypeAdvisor`](../../../src/Services/Domovoy.AutomationService/Ml/ArchetypeAdvisor.cs) +
> `POST /api/ml/classify-archetypes` (прокси `MlController`) — выявляет **расхождения** (устройства, которые модель
> типизировала бы иначе → кандидаты на пересмотр); не мутирует read-модель (как 1F «предлагай, не действуй»).
> WebUI `/models`: кнопка «Классифицировать» + список расхождений. **Архетипы в предложениях (2F):** `DeviceSnapshot`
> обогащён capabilities+архетипом; DiscoveryEngine аннотирует rationale парой `[motion → light]`. 4 юнит-теста
> классификатора (124 .NET-юнит). **Дальше:** обучение на override-корректировках как отдельный сигнал.

### Эпик 2E. Модель ролей ✅ (без enforcement)
- Локальные пользователи/роли/права (манифест плагина уже несёт `permissions`); назначение ролей. **Без** логина/токенов/сессий — enforcement в Фазе 3.
- **DoD:** CRUD пользователей/ролей + привязка прав; модель готова к будущему enforcement; в dev ничего не блокирует.

> **✅ Реализовано (Epic 2E — модель, без enforcement).** **Контракт** `Domovoy.Contracts/Security/`:
> [`WellKnownPermissions`](../../../src/Common/Domovoy.Contracts/Security/Permissions.cs) — **открытый** словарь
> прав (`devices.view/control`, `zones/modes/automations/blocks/models/plugins/users.manage`,
> `proposals.approve`, `activity.view`, `system.admin`), открыт как capability-модель (плагин может запросить
> право вне списка — манифесты уже несут `Permissions` строками); [`Role`](../../../src/Common/Domovoy.Contracts/Security/Role.cs)
> (имя + описание + `IsBuiltIn` + список прав) и [`User`](../../../src/Common/Domovoy.Contracts/Security/User.cs)
> (identity + `RoleIds` + `Enabled`, **без пароля/логина/сессии** — аутентификация в Фазе 3). **DbGateway** —
> источник истины: [`RolesEndpoints`](../../../src/Gateway/Domovoy.DbGateway/Endpoints/RolesEndpoints.cs)
> (`/api/roles` CRUD + `GET /permissions` = словарь для UI; built-in роль нельзя удалить, права редактируемы;
> при удалении роли она снимается со всех пользователей), [`UsersEndpoints`](../../../src/Gateway/Domovoy.DbGateway/Endpoints/UsersEndpoints.cs)
> (`/api/users` CRUD), [`SecuritySeeder`](../../../src/Gateway/Domovoy.DbGateway/Services/SecuritySeeder.cs)
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
> детерминированные):** [`InformationTheory`](../../../src/Services/Domovoy.AutomationService/Services/Discovery/InformationTheory.cs)
> (MI и **условная** MI в натах — ловит нелинейность и снимает конфаундер), [`ChiSquared`](../../../src/Services/Domovoy.AutomationService/Services/Discovery/ChiSquared.cs)
> (G-тест `G=2·N·MI` → p-value через регуляризованную неполную гамму = χ²-survival; Ланцош/цепная дробь),
> [`Statistics`](../../../src/Services/Domovoy.AutomationService/Services/Discovery/Statistics.cs) (Benjamini-Hochberg
> FDR). **Ядро воронки** [`PatternMiner.Mine`](../../../src/Services/Domovoy.AutomationService/Services/Discovery/PatternMiner.cs)
> (чистая функция, Stage 0→3): **Stage 0** — бакетизация истории в слоты, сенсор сэмплируется *как-of начало
> слота*, человеческие on-действия — *внутри* слота (сенсор всегда **предшествует** действию → уважает стрелу
> времени, снимает reverse-causality by construction); **Stage 1** — по каждой паре сенсор×актуатор условная
> `I(сенсор; действие | время-суток)` → G-тест p-value → **BH-FDR** по всем парам (здесь дохнут конфаундеры и
> случайные пары); **Stage 2** — для выжившей пары ищется предсказывающее условие: булев сенсор → «active»,
> числовой → **порог по терцилям** (`illuminance < t1` = «темно»→свет), опц. страж по времени суток; **Stage 3**
> — гейты support/confidence/**lift** (÷ base rate), учитель только `triggerSource=user` (без ML-on-ML/self-
> fulfilling), устройство не привязывается к себе. **Сервис-обёртка** [`DiscoveryEngine`](../../../src/Services/Domovoy.AutomationService/Services/Discovery/DiscoveryEngine.cs)
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
> **✅ Хвост (2026-07-06):** **Granger-скрининг** [`GrangerCausality`](../../../src/Services/Domovoy.AutomationService/Services/Discovery/GrangerCausality.cs)
> — дискретный LR G-тест на счётчиках (вложенные мультиномиальные модели: `P(action_t|action_{t-1})` vs
> `+sensor_{t-1}` → χ²), опциональный гейт в Stage 1 (`DiscoveryGrangerAlpha`, **off by default** — строже
> as-of MI, требует плотной consecutive-slot истории; 3 юнит-теста). **Тип B** — майнер уставок-предпочтений
> [`SetpointPreferenceMiner`](../../../src/Services/Domovoy.AutomationService/Services/Discovery/SetpointPreferenceMiner.cs):
> находит стабильные user-заданные числовые уставки (`temperature_setpoint`) по 6ч-бакетам (support+stddev,
> anti-loop=только user) → DiscoveryEngine эмитит **time-triggered** предложение «в HH:00 установить cap=V»
> (переиспользует Rule-proposal + replay-валидацию); 4 юнит-теста (131 .NET-юнит). **Тип C** (feedforward
> `возмущения→u_ss` для контуров 1H) — остаётся Фазе 3: нужны closed-loop данные контура + inverse-plant.

### Эпик 2G. Центр активности (события + логи + уведомления) ✅ (v1, без каналов доставки)
- Переосмысление пустого `/logs`: единый **читаемый/искомый/фильтруемый** вид — device-события (`/api/events`), история автоматизаций (`auto_history`), ops-логи (Serilog Mongo-sink `ops_logs`, **раздельно** с доменными по P0-5, объединение на уровне запроса/UI), алерты. Фильтры по источнику/severity/устройству/времени. Каналы доставки (push/telegram/email) — под-часть, ближе к концу.
- **DoD:** один экран отвечает «что произошло и почему», ищется/фильтруется; аномалия датчика (из 2B) и сбой автоматизации видны; ≥1 канал доставки.

> **✅ Реализовано (Epic 2G — v1).** **Ops-логи в Mongo:** Serilog Mongo-sink (`Serilog.Sinks.MongoDB` 5.4,
> driver-2.x совместимый) в общем [`SerilogBootstrap`](../../../src/Common/Domovoy.Common/Logging/SerilogBootstrap.cs)
> — env-gated `SERILOG_MONGO_URI` (best-effort, try/catch), пишет в capped-коллекцию `ops_logs` (100 МБ /
> 200k док.), раздельно с доменным event-log (P0-5). Env проставлен всем .NET-сервисам в compose. **Единый
> фид:** [`ActivityEndpoints`](../../../src/Gateway/Domovoy.DbGateway/Endpoints/ActivityEndpoints.cs) —
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
> [`positioning_ru.md`](../positioning_ru.md)) через сам интерфейс — но **через поведение, а не декор**.
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
> `AutomationService/Services/Assistant/`: интерфейс [`IAssistantConnector`](../../../src/Services/Domovoy.AutomationService/Services/Assistant/IAssistantConnector.cs)
> с двумя способностями **вне контура управления** — `AuthorRuleAsync` (естественный язык → `Proposed`-правило
> через очередь 2C + валидация реплеем 1F) и `ExplainAsync` (атрибуция 1F «почему» → фраза). Поставляемая
> реализация — no-op [`DisabledAssistantConnector`](../../../src/Services/Domovoy.AutomationService/Services/Assistant/DisabledAssistantConnector.cs):
> каждый запрос деградирует мягко (`Available=false` + сообщение), а не падает. **Фича-флаг**
> [`AssistantOptions`](../../../src/Services/Domovoy.AutomationService/Configuration/AssistantOptions.cs) (`Assistant:Enabled`
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
> [`docs/architecture/roadmap/phase-0-foundation.md`](phase-0-foundation.md) (раздел «Ход исполнения», capability-миграция §4) и memory `domovoy-native-protocol`) и попасть в уже
> готовый `DomovoyNativeAdapter` **без изменений на сервере**. Этот эпик реализует путь (1): адаптер
> укладывается в существующий [`IProtocolAdapter`](../../../src/Services/Domovoy.Connectivity/Adapters/IProtocolAdapter.cs)
> (Эпики [1C](phase-1-automation.md#эпик-1c-integration-sdk--внепроцессные-плагины-поверх-шины)/[1D](phase-1-automation.md#эпик-1d-capability-адаптеры-под-целевые-домены))
> по образцу [`Zigbee2MqttAdapter`](../../../src/Services/Domovoy.Connectivity/Adapters/Zigbee2MqttAdapter.cs) —
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

> **✅ Реализовано (Epic 2J — v1).** Новый [`EspHomeMqttAdapter : IProtocolAdapter`](../../../src/Services/Domovoy.Connectivity/Adapters/EspHomeMqttAdapter.cs)
> в `Domovoy.Connectivity` (регистрируется в DI, `AdapterManager` подхватывает автоматически; вся логика
> publish/subscribe — внутри адаптера, как у Z2M). **Codec** [`EspHomeCodec`](../../../src/Services/Domovoy.Connectivity/Adapters/EspHomeCodec.cs)
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
> локально офлайн ([`SunCalculator`](../../../src/Services/Domovoy.AutomationService/Services/SunCalculator.cs) —
> астрономическая формула по lat/lon), поэтому **никакого внешнего sunrise-API/таблиц не добавляем**.
> Геокодер (Nominatim) используется **только и опционально** — превратить название/клик по карте в координаты
> при редактировании; ручной ввод lat/lon всегда работает офлайн. Таймзона выводится из координат офлайн через
> **GeoTimeZone** (NuGet, встроенные tz-шейпы) с ручным override.
> **Backend:** контракт [`SiteLocation`](../../../src/Common/Domovoy.Contracts/Home/SiteLocation.cs) (singleton POCO,
> Id="current"). DbGateway [`SettingsEndpoints`](../../../src/Gateway/Domovoy.DbGateway/Endpoints/SettingsEndpoints.cs)
> (`/api/settings`): GET/PUT `location` (upsert в `site_location`, PUT выводит tz через GeoTimeZone если не
> закреплена), GET `timezone`, GET `geocode`/`reverse-geocode`; `IGeocoder`/`NominatimGeocoder`
> ([Services/Geocoder.cs](../../../src/Gateway/Domovoy.DbGateway/Services/Geocoder.cs)) — typed HttpClient,
> все ошибки глушатся в пустой результат (offline-first). ApiGateway
> [`SettingsController`](../../../src/Gateway/Domovoy.ApiGateway/Controllers/SettingsController.cs) прокси.
> **AutomationService:** `SunCalculator` теперь **runtime-mutable** (lat/lon за `volatile`-ссылкой на immutable
> record; `RefreshLoop.RefreshLocation` тянет `GetLocationAsync` каждый цикл и перенацеливает калькулятор при
> изменении; `appsettings` остаётся seed/fallback). **WebUI:** маршрут `/settings` + пункт-шестерёнка в навигации;
> [`pages/Settings.tsx`](../../../src/UI/WebUI/src/pages/Settings.tsx) — карточка Location (поиск места → геокодер;
> **Leaflet-карта** клик/drag для выбора; ручной lat/lon; метка; авто-tz + override; сохранение) + карточка
> Appearance; [`components/settings/LocationMap.tsx`](../../../src/UI/WebUI/src/components/settings/LocationMap.tsx)
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
> железа». Выбран **отдельный [`SystemSensorService`](../../../src/Services/Domovoy.AutomationService/Services/SystemSensorService.cs)**
> (а не control-block — блок требует ручного инстанса; системные сенсоры должны существовать всегда):
> `AdapterSource="System"`, детерминированные id `DeviceIdFactory.Derive("System","<kind>")`, анонс раз +
> republish состояния раз в минуту. **Sun:** capabilities `sun_elevation`/`sun_azimuth`/`is_dark`/`is_day`/
> `sunrise`/`sunset`; `SunCalculator.Position(instant)` = **алгоритм NOAA** (офлайн). **Time:** `time_of_day`
> (локальные минуты от полуночи) + `clock` (HH:mm). **Calendar:** `day_of_week`/`is_weekend`/`is_holiday`/`date`.
> Локальное время — через новый синглтон [`SiteContext`](../../../src/Services/Domovoy.AutomationService/Services/SiteContext.cs)
> (tz из `SiteLocation`, 2K); календарь — через [`CalendarContext`](../../../src/Services/Domovoy.AutomationService/Services/CalendarContext.cs)
> (выходные + даты праздников), оба обновляются `RefreshLoop`. Контракт
> [`CalendarSettings`](../../../src/Common/Domovoy.Contracts/Home/CalendarSettings.cs) (singleton: `WeekendDays` +
> `Holidays`); `SettingsEndpoints` GET/PUT `/api/settings/calendar` + POST `…/import?country=&year=` — импорт
> праздников через **Nager.Date** ([`HolidayImporter.cs`](../../../src/Gateway/Domovoy.DbGateway/Services/HolidayImporter.cs),
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
> статусе `Blocked` (требует GPU), т.е. система плагинов ([Эпик 1C](phase-1-automation.md#эпик-1c-integration-sdk--внепроцессные-плагины-поверх-шины))
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

> **Ключевой инсайт архитектуры — сырьё уже есть.** [`ActivityEndpoints`](../../../src/Gateway/Domovoy.DbGateway/Endpoints/ActivityEndpoints.cs)
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

