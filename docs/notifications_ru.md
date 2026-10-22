# Дисциплина уведомлений (Эпик 3F)

> Как Домовой решает, **куда, как часто и с какими кнопками** доходит уведомление. Ров — заложить
> дисциплину с первого дня, а не наращивать исторически (боль конкурентов: notification fatigue → «крик
> волка»). Опирается на таксономию NN/g и на уже готовый транспорт 2G/2M.2/2O.

## Модель

Каждое уведомление несёт (`NotificationMessage` → `NotificationRaisedV1`):

- **Категорию** (`NotificationCategories`, таксономия NN/g):
  - `reactive` — что-то случилось и требует внимания (сработало правило, сброшена нагрузка);
  - `proactive` — система что-то нашла/предлагает (закономерность 2F, предложение ML);
  - `optimization` — низкоприоритетная подсказка по эффективности («включи бойлер в дешёвые часы»).
- **Severity** (`NotificationSeverities`): `info` / `warning` / `critical`. **`critical` = класс безопасности**:
  никогда не приглушается, никогда не режется лимитом, всегда пробивается на заметный канал.
- **Actions** (`NotificationAction[]`) — actionable-кнопки (см. ниже).
- **DedupKey** — стабильный ключ для дедупликации повторов.

## Каналы и видимость

Каналы (`INotificationChannel`, реестр 2G) объявляют **видимость** (`NotificationVisibility`):

- **Quiet** — виден только при открытом приложении: LAN-баннер SignalR (`lan`).
- **Prominent** — доходит вне приложения: push/ntfy (`ntfy`), Telegram, вебхук.

## Политика доставки (чистая логика — `NotificationPolicy`)

Порядок решений (юнит-тесты `NotificationPolicyTests`):

1. **Маршрутизация по типам (opt-out).** По умолчанию каждый включённый канал получает всё; пользователь
   **приглушает** конкретные пары (категория, канал) — сузить, но не «расширить в сюрприз». Safety
   игнорирует приглушение.
2. **Пол безопасности.** `critical` при `SafetyFloorEnabled` всегда добавляется на заметный канал, даже
   если он приглушён. Если заметных каналов нет вовсе — доставка на тихий + предупреждение в лог
   (`safety_quiet_only`).
3. **Rate-limit / dedup.** Повтор с тем же ключом (`DedupKey`, иначе `категория|title|body`) в пределах
   окна категории отбрасывается (против «крика волка»). Дефолт окна: reactive 60 с, proactive 900 с,
   optimization 3600 с. **Safety не ограничивается никогда.**

`NotificationDispatcher` держит дедуп-словарь (с чисткой) и раздаёт сообщение выбранным каналам; ошибка
канала изолирована и не рушит исполнение правил. Настройки живут в singleton `notification_settings`
(DbGateway `SettingsEndpoints`), синкаются `RefreshLoop` → `NotificationRuntimeState` (действует без
рестарта; офлайн-first — при недоступности шлюза держим last-known).

## Actionable-уведомления

`NotificationAction(Id, Label, Kind, Params)`. Серверные виды исполняются **с атрибуцией актора** (тот, кто
нажал — по той же actor-строке `user:{id}`, что и ручная команда), клиентские — в браузере:

| Kind | Что делает | Params |
|---|---|---|
| `approve_proposal` | одобрить предложение 2C | `proposalId` |
| `reject_proposal` | отклонить предложение 2C | `proposalId` |
| `device_command` | команда устройству | `deviceId`, `capabilityId`, `value` |
| `set_mode` | сменить режим дома 1G | `mode` |
| `open` | навигация WebUI (клиент) | `route` |

Маппинг action → исполнение — чистый `NotificationActionRouter` (`NotificationActionRouterTests`);
`NotificationsController.ExecuteAction` (`POST /api/notifications/action`) лишь выполняет план: bus-команда
`DeviceCommandV1` (source = актор) или форвард в DbGateway (approve/reject/mode). WebUI-баннер рендерит
кнопки и шлёт нажатие в этот эндпоинт.

## UI

- **Баннер** (`components/common/Notification.tsx`): рендерит actionable-кнопки; actionable-уведомление
  показывается баннером даже на `info` (иначе кнопку не нажать), критичное висит до закрытия.
- **Настройки** (`/settings` → «Дисциплина уведомлений», `NotificationSettingsEditor`): матрица
  маршрутизации категория×канал, антиспам-интервалы по категориям, тумблер пола безопасности.

## DoD — статус

Уведомление несёт тип и per-type канал ✅; safety не уходит только тихим каналом ✅; повторы
дедупятся/троттлятся ✅; actionable-кнопка исполняет команду с атрибуцией актора ✅; «одобрить предложение
2C из уведомления» доступно ✅. Питает 2M.2/2O.

## Файлы

- Контракты: `Domovoy.Contracts/Notifications/{NotificationTaxonomy,NotificationSettings}.cs`,
  `NotificationRaisedV1.Actions` в `Messaging/Payloads.cs`.
- Дисциплина: `AutomationService/Services/Notifications/{NotificationPolicy,NotificationDispatcher,
  NotificationRuntimeState,INotificationChannel}.cs`; синк в `RefreshLoop`.
- Настройки: `DbGateway/Endpoints/SettingsEndpoints.cs` (`notification_settings`), прокси
  `ApiGateway/Controllers/SettingsController.cs`.
- Actionable: `ApiGateway/Services/NotificationActionRouter.cs`, `Controllers/NotificationsController.cs`,
  `Services/NotificationRelayService.cs`.
- WebUI: `store/uiStore.ts`, `components/common/Notification.tsx`,
  `components/notifications/NotificationHubListener.tsx`, `components/settings/NotificationSettingsEditor.tsx`,
  `api/notifications.ts`.
- Тесты: `NotificationPolicyTests`, `NotificationActionRouterTests` (+ существующий `NotificationChannelsTests`).
