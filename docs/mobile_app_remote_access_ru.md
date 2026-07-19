# Мобильное приложение и внешний доступ

Документ описывает Android-оболочку Домового и безопасный доступ к дому извне при сером IPv4 (CGNAT).
Транспорт: **IPv6-директ — основной**, **SSH reverse-tunnel к VPS — запасной**. Предпосылка ко всему —
**включённая аутентификация** (сделана первой). Соответствует roadmap Эпик 2O.

## Компоненты

| Слой | Что | Где в репозитории |
|---|---|---|
| Аутентификация | логин/JWT/enforcement поверх модели ролей 2E | `src/Gateway/*`, `src/Common/Domovoy.Contracts/Security` |
| PWA | устанавливаемый WebUI, service worker | `src/UI/WebUI` (vite-plugin-pwa) |
| Приложение | тонкая Capacitor-оболочка + менеджер подключения | `mobile/` |
| Edge-TLS | Caddy :443 перед WebUI | `deploy/caddy`, профиль `edge` |
| IPv6-директ | AAAA-DDNS + firewall | `deploy/ipv6-ddns` |
| SSH-fallback | autossh → VPS | `deploy/autossh`, `deploy/vps`, профиль `tunnel` |

---

## 1. Аутентификация (предпосылка, сделана первой)

Раньше API был открыт (`JwtSettings:Enabled=false`, CORS настежь). Теперь:

- **Пароли** к пользователям (`User.Username` + PBKDF2-хеш, `Domovoy.DbGateway.Services.PasswordHasher`, без внешних крипто-зависимостей).
- **JWT** access (60 мин) + refresh (30 дней, отдельная audience). Минтит `AuthController` (ApiGateway), проверяет credentials `DbGateway /api/auth/verify-credentials`.
- **Enforcement**: fallback-политика «нужен вход» на всём API + permission-политики на чувствительных контроллерах (device-control, users, roles, system, plugins, backup, ml). SignalR-хаб — токен в query (редактируется в логах).
- **Начальный админ**: seeder создаёт `admin` с паролем из `SECURITY__BOOTSTRAPADMINPASSWORD` (по умолчанию `admin` + предупреждение в лог).
- Флаг `JWTSETTINGS__ENABLED`: **on в docker-compose** (боевой posture), off в dev/тестах.

**Что сделать перед боем:**
1. В `.env`: `JWT_SECRET_KEY=$(openssl rand -base64 48)` и `DOMOVOY_ADMIN_PASSWORD=<свой>`.
2. Поднять стек, войти как `admin`, сменить пароль, завести пользователей (страница «Пользователи»: имя входа + «Задать пароль»).
3. Отзыв сессии потерянного телефона: отключить пользователя (галка «Активен») — refresh перестаёт работать в пределах жизни access-токена (~1 ч).

## 2. PWA

`vite-plugin-pwa` (Workbox). Service worker кэширует оболочку приложения, но **никогда** `/api` и `/hub`
(состояние устройств всегда живое), `/locales` — network-first. Установка «на экран» на Android/десктоп без
единой строки нативного кода. Иконка — `public/domovoy-icon.svg` (maskable-safe). nginx отдаёт `sw.js`,
`registerSW.js`, `manifest.webmanifest` с `no-cache` (иначе обновление залипнет).

## 3. Приложение (Capacitor)

`mobile/` — тонкая оболочка, загружающая **живой UI** (не бандл). Лаунчер (`mobile/www/`) онбордит адреса и
выбирает транспорт (менеджер подключения). Сборка — `mobile/README.md`. APK ставится сайдлоадом.

**Менеджер подключения** (`mobile/www/connection.js`): пробинг `GET {origin}/health` в порядке приоритета
**LAN → IPv6 → SSH**, первый ответивший за 1.5 с открывается в WebView. UI всегда same-origin — ему всё равно,
какой транспорт его принёс.

---

## 4. Внешний доступ

### 4a. Edge-TLS (Caddy) — общий для обоих путей

Единый публичный вход `:443` перед WebUI. Одна TLS-точка и для IPv6-пинхола, и для цели SSH-туннеля.

```bash
# .env: DOMOVOY_DOMAIN=home.example.com  ACME_EMAIL=you@example.com
docker compose --profile edge up -d caddy
```

Auto-HTTPS через HTTP-01 (нужны 80+443 наружу). Если открывать только 443 — соберите Caddy с DNS-плагином и
переведите на DNS-01 (в `deploy/caddy/Caddyfile` есть заготовка).

### 4b. IPv6-директ (основной путь)

**Шаг 0 — проверить, что провайдер даёт настоящий global IPv6** (а не ULA/CGNAT-over-v6):
```bash
ip -6 addr show scope global | grep -viE 'fe80|^ *inet6 (fc|fd)'
# должен быть адрес 2000::/3; проверить доступность извне: ping6 с мобильной сети / ipv6-test.com
```
Если global IPv6 нет — основным де-факто станет SSH-fallback (4c).

**Шаг 1 — firewall-пинхол**: на роутере разрешить **только 443/tcp** (и 80/tcp для ACME) на IPv6 хоста
Домового, остальное закрыто. IPv6 маршрутизируется до устройства напрямую — firewall тут единственный периметр.

**Шаг 2 — AAAA-DDNS**: префикс может меняться по аренде. `deploy/ipv6-ddns/update-aaaa.sh` находит global IPv6
и обновляет AAAA-запись (пример под Cloudflare API; замените `update_dns` под своего провайдера). Запуск из
systemd-timer/cron каждые несколько минут:
```bash
# /etc/domovoy-ddns.env: DDNS_HOSTNAME, CF_API_TOKEN, CF_ZONE_ID
*/5 * * * * . /etc/domovoy-ddns.env && /opt/domovoy/deploy/ipv6-ddns/update-aaaa.sh
```
Для стабильности адреса задайте фиксированный interface-identifier (token) на интерфейсе.

### 4c. SSH-fallback к VPS (запасной путь)

Для IPv4-only клиентов / когда IPv6 не добивает. Модель — HA-аддон владельца (autossh reverse-tunnel).

**На VPS** (`deploy/vps/`):
```bash
# .env: DOMOVOY_DOMAIN, ACME_EMAIL, TUNNEL_USER, TUNNEL_SSH_PORT
docker compose -f deploy/vps/docker-compose.yml up -d
# в ./sshd/sshd_config включить: GatewayPorts clientspecified, AllowTcpForwarding yes; перезапустить sshd
```

**Дома**:
```bash
docker compose --profile edge --profile tunnel up -d
docker compose logs autossh   # первый запуск печатает restricted-ключ
# вставить строку restrict,port-forwarding,permitopen=... в ./authorized_keys на VPS, перезапустить autossh
```
autossh держит `-R 127.0.0.1:8443:caddy:443` к VPS; Caddy на VPS публикует `https://<домен>` и проксирует в
туннель (TLS-в-TLS, `tls_insecure_skip_verify` для внутреннего плеча). Reconnect-петля + `ServerAliveInterval=30`.

---

## Порядок боевого включения

1. **Auth**: `.env` (`JWT_SECRET_KEY`, `DOMOVOY_ADMIN_PASSWORD`), поднять стек, войти, сменить пароль, завести пользователей.
2. **Caddy edge**: домен + `--profile edge`, проверить `https://<домен>` из LAN.
3. **IPv6**: проверить global IPv6 → firewall 443 → DDNS-таймер → проверить с мобильной сети.
4. **SSH-fallback**: VPS-приёмник → ключ → `--profile tunnel` → проверить из IPv4-only сети.
5. **Приложение**: собрать APK (`mobile/`), в онбординге указать LAN + IPv6 + SSH-адреса.

## 5. Уведомления (Этап 4)

Реализованы два канала поверх существующего `NotificationDispatcher` (AutomationService):

- **LAN-баннер (2M.2)** — `SignalRChannel` публикует `NotificationRaisedV1` на шину (`domovoy.events` /
  `notification.raised`); `NotificationRelayService` (ApiGateway) ретранслирует на `DeviceHub` → WebUI
  (`NotificationHubListener`) показывает баннер на любой странице. **Включён по умолчанию**, полностью локально,
  переживает выключенный интернет.
- **Push вне LAN (2O.4)** — `NtfyChannel` POST-ит на self-hosted **ntfy** (`{ServerUrl}/{Topic}`, severity →
  priority/tags), для спящего/вне-LAN телефона через UnifiedPush. **Off by default**; ntfy — опциональный
  сервис compose (профиль `push`), в стек не линкуется (только HTTP-вызовы). Настройка:
  `NOTIFICATIONS__NTFY__{ENABLED,SERVERURL,TOPIC,TOKEN}` у automation-service.

```bash
docker compose --profile push up -d ntfy   # поднять self-hosted ntfy
# затем раскомментировать NOTIFICATIONS__NTFY__* у automation-service и указать TOPIC
```

**Осталось по push:** клиент **UnifiedPush** в Android-оболочке (подписка на тот же топик ntfy → пробуждение
WebView/баннер в фоне). ntfy нужно сделать внешне доступным (маршрут через Caddy-edge или свой поддомен) — иначе
push добивает только в LAN. Дисциплина уведомлений (таксономия/дедуп/per-type каналы) — Эпик **3F**; поле
`Category` в `NotificationRaisedV1` уже заложено под неё.

## 6. Режим киоска (Этап 5)

Web-часть реализована в WebUI: `/settings` → «Режим киоска». Панель:
- пинится на выбранный экран (главная или конкретный кастомный дашборд);
- прячет всю навигацию (`Layout` рендерит `KioskShell` без сайдбара/аппбара);
- по бездействию возвращается на закреплённый экран (`idleReturnSeconds`) и затемняет экран
  (`dimSeconds`, полноэкранный оверлей — web-аналог гашения подсветки; касание пробуждает);
- выход — скрытое долгое нажатие (2 с) в левом верхнем углу с подтверждением.

Конфиг хранится локально (`domovoy.kiosk`), поэтому перезагрузка возвращает панель в киоск. Идентичность —
встроенная роль **`kiosk`** (2E: `devices.view`+`devices.control`, ничего больше).

**Осталось по киоску — нативный слой Android** (поверх web-режима, в Capacitor-оболочке):
- **screen pinning / lock-task** — полный киоск через device-owner (провижининг adb) либо user-initiated Screen
  pinning; запирает выход из приложения;
- **автозапуск** по `BOOT_COMPLETED`;
- **always-on** экран (`@capacitor-community/keep-awake`, проверить лицензию).
Приложение просто открывает `/settings`, включаешь режим киоска — дальше UX ведёт WebUI.
Улучшение: dim/wake по присутствию (1G) и времени (Sun/Time 2L) вместо чистого таймера бездействия.

## Осталось (следующие итерации)

- **Клиент UnifiedPush** в приложении + внешняя доступность ntfy (Этап 4).
- **Нативный киоск Android** — screen pinning / автозапуск / keep-awake (см. выше).
- **Централизованный реестр панелей** (`panels`-коллекция, управление из `/settings`) — для нескольких панелей;
  одиночной панели хватает локального конфига.
- **App-level hardening**: биометрический замок, secure storage токена (Android Keystore), certificate pinning
  для самоподписанных сценариев.
