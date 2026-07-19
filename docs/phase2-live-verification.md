# Живая верификация Фазы 2 — точечный чек-лист

> **Зачем.** Ядро (Фаза 1) прогнано вживую 2026-07-06 (3-оконный сценарий + реальный Zigbee, см.
> [`runbook.md`](runbook.md)). Но пласт, влитый в `develop` **2026-07-14 и позже**, наслоился *после*
> того прогона и вживую против реального RabbitMQ/Mongo не проверялся. Юнит- и офлайн-тесты этого не
> ловят — они мокают шину и БД. Здесь проверяется ровно то, что добавило **новые формы документов Mongo
> и новые рантайм-пути**: сериализация BSON под реальным драйвером, маршрутизация шины, SignalR,
> восстановление состояния между рестартами.
>
> **Это НЕ полный регресс.** Базовые потоки (discovery/state/команды/правила) считаем проверенными
> регулярными прогонами и сценарием из `runbook.md`. Здесь — только 4 новых пути.
>
> **Как пользоваться.** Подними стек и эмулятор по [`runbook.md`](runbook.md) §2–3. Пройди 4 блока ниже.
> На каждом успешном пункте — сними устаревший штамп «не прогонялось вживую» в источниках (см.
> «Что обновить при успехе» в конце). Если что-то падает — записывай симптом, это и есть находка.

## Сводка (отметки прохождения)

| # | Путь | Эпик | Главный риск | Статус |
|---|---|---|---|---|
| 1 | Батч-эндпоинты дашборда | dashboard-fill | агрегация `$dateTrunc` под реальным Mongo, N+1 → батч | ⬜ |
| 2 | Персистентность состояния блоков | 2Q | восстановление `block_state` после рестарта | ⬜ |
| 3 | Рантайм ML-задач | 2P | `ml_tasks`/`ml_models` round-trip, train → serve уставки | ⬜ |
| 4 | Дневник дома | 2N | материализация `home_story`, `narrative_*` round-trip | ⬜ |

**Порты (из runbook):** WebUI `http://localhost` · ApiGateway/Swagger `http://localhost:5000` ·
DbGateway/OpenAPI `http://localhost:5001` · Mongo `mongodb://localhost:27017/domovoy` · эмулятор `:5080`.

Быстрый доступ к Mongo:
```powershell
docker compose exec mongodb mongosh domovoy --eval "db.getCollectionNames()"
```

---

## 1. Батч-эндпоинты дашборда (dashboard-fill)

**Что проверяем.** Главная `/` перешла на 2 батч-эндпоинта вместо N+1 запросов на плитку. Риск — реальная
Mongo-агрегация (`$dateTrunc`/`$group`) и форма ответа под живым драйвером.

**Эндпоинты (через ApiGateway):** `POST /api/telemetry/aggregate/batch`, `POST /api/events/latest-by-device`.

**Шаги (UI):**
1. Открой `http://localhost/` — дождись устройств от эмулятора (5–10 c).
2. Проверь верхний **HomeStateBand** (полоса состояния дома), **DomovoyRail** справа (активность/предложения;
   кнопка «Позже» локально прячет элемент), плитки **DeviceTile** со **спарклайном** и **чипом автора**.
3. DevTools → Network: убедись, что телеметрия тянется **одним** `aggregate/batch`, а не запросом на плитку,
   и что есть **один** `events/latest-by-device`. Оба должны вернуть `200`.

**Проверка API напрямую (headless, без UI):**
```powershell
# подставь реальный deviceId числового сенсора из GET /api/capability-devices
$body = @{ series = @(@{ deviceId='<id>'; capabilityId='temperature' }); bucket='hour'; agg='avg'; hours=24 } | ConvertTo-Json -Depth 6
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/telemetry/aggregate/batch -Body $body -ContentType 'application/json'
```
> ⚠️ Точную форму тела сверь по [`HistoryEndpoints.cs`](../src/Gateway/Domovoy.DbGateway/Endpoints/HistoryEndpoints.cs)
> (`AggregateBatchRequest`) — правь поля под фактический контракт, если отличается.

**Критерий:** `/` рендерится со спарклайнами и чипами; батч-эндпоинты возвращают корректные серии; в Network
нет N+1 (по одному батч-запросу, а не по одному на плитку). ✅ / симптом: __________

---

## 2. Персистентность состояния блоков (2Q)

**Что проверяем.** Блоки со внутренним состоянием снапшотятся в коллекцию `block_state` (периодически ~2 мин +
на graceful shutdown) и **восстанавливаются при старте**. Риск — round-trip состояния и что рестарт не сбрасывает
контур в холодное состояние.

**Шаги:**
1. На `/blocks` создай блок с явно наблюдаемым состоянием — проще всего **`counter`** (считает импульсы) или
   **`latch`** (SR-триггер). Привяжи вход к capability эмулятора (напр. `occupancy` датчика движения).
2. Погоняй состояние: несколько раз щёлкни вход в UI эмулятора (`:5080`) → у `counter` значение растёт
   (видно на `/blocks` / `/devices` как виртуальное устройство).
3. Дай снапшоту записаться и убедись, что он есть:
   ```powershell
   docker compose exec mongodb mongosh domovoy --eval "db.block_state.find().pretty()"
   # либо через API: GET http://localhost:5000/api/block-state
   ```
4. Рестарт сервиса блоков (SIGTERM → финальный снапшот при выходе):
   ```powershell
   docker compose restart automation-service
   ```
5. После подъёма проверь значение блока — **счётчик/латч не должен обнулиться**.

**Критерий:** в `block_state` есть документ блока; после `restart automation-service` состояние продолжается,
а не стартует с нуля. ✅ / симптом: __________

---

## 3. Рантайм ML-задач (2P)

**Что проверяем.** Обучение управляется рантайм-коллекцией `ml_tasks` (а не env), модели пишутся в `ml_models`
по `(kind, target, scope)`; путь задача → train → serve уставки блоком. Риск — BSON round-trip задач/моделей и
живой цикл обучения.

**Предусловие:** нужна накопленная история телеметрии/событий по целевой capability (иначе data-check зарубит
холодный старт). Погоняй эмулятор с меняющейся температурой некоторое время либо используй уже накопленные данные.

**Шаги (UI `/models`):**
1. Создай **ML-задачу** мастером (target-capability, scope зоны/глобально, окно истории, клампы).
2. Нажми **Train now** (или `POST /api/ml/train?taskId=<id>`) → в `ml_models` появляется версия для этой задачи.
3. Посмотри **scorecard/backtest** (`GET /api/ml/backtest?days=`) — MAE/RMSE, прогноз-vs-факт рисуется.
4. (Опц.) Привяжи модель к блоку `ml_setpoint`/`ml_governor` на `/blocks` и убедись, что он эмитит уставку
   (виден как виртуальное устройство, значение обновляется на тике).

**Проверка Mongo:**
```powershell
docker compose exec mongodb mongosh domovoy --eval "db.ml_tasks.find().pretty()"
docker compose exec mongodb mongosh domovoy --eval "db.ml_models.find({}, {Artifact:0, artifact:0}).pretty()"
```

**Критерий:** задача сохраняется и читается; train регистрирует модель в `ml_models`; backtest отдаёт метрики;
привязанный ML-блок отдаёт уставку. ✅ / симптом: __________

---

## 4. Дневник дома (2N)

**Что проверяем.** Конвейер `DiaryMiner → … → HouseDiaryBuilder` материализует дни в `home_story`; персонализация
имён — `narrative_entities`; ротация — `narrative_state`. Риск — round-trip этих документов и рендер под живым Mongo.

**Эндпоинты:** `GET /api/home-story`, `POST /api/home-story/preview`, `POST /api/home-story/rebuild?date=yyyy-MM-dd`.

**Предусловие:** нужны события за целевой день (`device_events`). Пошевели устройства в эмуляторе, чтобы
накопились дельты.

**Шаги:**
1. На `/logs` включи тумблер **«Дневник»**; задай имена духа/жильцов в диалоге (пишутся в `narrative_entities`).
2. Форс-сборка дня с событиями:
   ```powershell
   Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/home-story/rebuild?date=2026-07-17"
   ```
3. Открой ленту дневника на `/logs` — день отрендерился связным русским текстом (не пусто, если события были).

**Проверка Mongo:**
```powershell
docker compose exec mongodb mongosh domovoy --eval "db.home_story.find().pretty()"
docker compose exec mongodb mongosh domovoy --eval "db.narrative_entities.find().pretty()"
docker compose exec mongodb mongosh domovoy --eval "db.narrative_state.find().pretty()"
```

**Критерий:** `rebuild` материализует записи в `home_story`; лента рендерится; имена из `narrative_entities`
подхватываются. ✅ / симптом: __________

---

## Что обновить при успехе (снять устаревший штамп)

Когда пункт зелёный — убери «не прогонялось вживую» там, где он стоит:

- **Роадмэп** [`architecture/roadmap.md`](architecture/roadmap.md) — блоки эпиков 2P / 2Q / 2N и строка
  dashboard-fill (пометь «live-verified <дата>»).
- **Память** `MEMORY.md` + файлы `domovoy-epic-2p-ml-tasks.md`, `domovoy-epic-2q-generalized-blocks.md`,
  `domovoy-epic-2n-plan.md`, `domovoy-dashboard-fill.md` (сейчас все несут «НЕ прогонялось вживую»).
- **`memory-bank/activeContext.md`** — раздел «Что следующее» (п.1 про верификацию рантайма).

## Если что-то упало

Живые прогоны в этом проекте уже ловили реальные баги (`JsonElement→Mongo` на правилах — 2C; writable-over-bus —
2D; залипший `IsOnline` — liveness). Симптом с этого чек-листа — это находка: зафиксируй устройство/шаги/лог
(`docker compose logs -f db-gateway automation-service api-gateway`) и заводи как задачу на фикс.
