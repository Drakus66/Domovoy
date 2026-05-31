# Domovoy — Runbook (запуск и проверка)

Практическое руководство: поднять стек, запустить эмулятор и прогнать сквозной 3-оконный сценарий.
Перед более глубоким копанием смотрите [`memory-bank/currentState.md`](../memory-bank/currentState.md)
(текущее состояние архитектуры) и [`docs/architecture/roadmap.md`](architecture/roadmap.md).

## Предусловия
- Docker Desktop с Compose v2.
- .NET 9 SDK — для локального запуска **эмулятора устройств** (он намеренно не в docker, это инструмент разработчика).
- Опционально: Zigbee USB-стик + `data/zigbee2mqtt/configuration.yaml`. Без них контейнер `zigbee2mqtt` будет рестартиться, остальное работает.

## 1. `.env`
В корне репозитория (значения по умолчанию совпадают с Z2M-конфигом, чтобы Zigbee2MQTT смог зайти на RabbitMQ-MQTT):

```env
RABBITMQ_DEFAULT_USER=user
RABBITMQ_DEFAULT_PASS=user
# Опционально — путь стика для будущего проброса через `devices:` в compose; сейчас контейнер
# zigbee2mqtt запускается с privileged: true и видит /dev/ttyUSB0 после usbipd-attach (см. §2b).
ZIGBEE_DEVICE_PATH=/dev/serial/by-id/usb-Itead_Sonoff_Zigbee_3.0_USB_Dongle_Plus_V2_<id>-if00-port0
```

Эти креды должны совпадать с `mqtt.user`/`mqtt.password` в [`data/zigbee2mqtt/configuration.yaml`](../data/zigbee2mqtt/configuration.yaml) (по умолчанию там `user/user`).

## 2. Запуск стека (без Zigbee)

```powershell
docker compose up -d rabbitmq mongodb prometheus db-gateway api-gateway connectivity-service unified-device-service webui
```

Состояние:

```powershell
docker compose ps
docker compose logs -f connectivity-service unified-device-service db-gateway api-gateway
```

## 2b. Запуск с Zigbee USB-стиком (Windows + WSL2)

Docker Desktop на Windows не видит USB-устройства хоста напрямую — стик нужно пробросить в WSL2 через
[**usbipd-win**](https://github.com/dorssel/usbipd-win/releases). После этого `zigbee2mqtt` (с `privileged: true`
в compose) увидит `/dev/ttyUSB0`.

### Один раз
1. Установите `usbipd-win` (релиз с GitHub или `winget install usbipd`).
2. Подключите стик и узнайте его `BUSID`:
   ```powershell
   usbipd list
   ```
   Найдите свою плату (напр. `Sonoff Zigbee 3.0 USB Dongle Plus`); запомните `BUSID` формата `X-Y` (на моей машине — `2-2`).
3. Разрешите проброс этого устройства (один раз, от **администратора**):
   ```powershell
   usbipd bind --busid 2-2
   ```

### После каждой перезагрузки / переподключения стика
Привязка `bind` сохраняется, а сам `attach` к WSL — нет:

```powershell
usbipd attach --wsl --busid 2-2
```

Проверка из WSL/контейнера: `ls /dev/ttyUSB*` — должен появиться `/dev/ttyUSB0`.

### Готовые скрипты в корне репозитория
- [`pre-startup.ps1`](../pre-startup.ps1) — выполняет `usbipd attach --wsl --busid 2-2`. **Если у вас другой `BUSID` — поправьте файл.**
- [`start-domovoy.bat`](../start-domovoy.bat) — обёртка: сначала `pre-startup.ps1`, потом `docker-compose up -d`.

Запуск одной командой:

```powershell
.\start-domovoy.bat
# либо вручную:
pwsh .\pre-startup.ps1
docker compose up -d
```

### Z2M-конфиг ([`data/zigbee2mqtt/configuration.yaml`](../data/zigbee2mqtt/configuration.yaml))
- `serial.port: /dev/ttyUSB0` — после usbipd-attach стик появляется именно тут.
- `mqtt.server: mqtt://rabbitmq:1883` + `user/user` — должно совпадать с `.env`.
- При смене `channel` / `pan_id` / `network_key` существующие Zigbee-устройства придётся перепаривать.

Z2M frontend: http://localhost:8081.

## 3. Эмулятор (локально, не в docker)

```powershell
cd src\Tools\Domovoy.DeviceEmulator
dotnet run
```

Эмулятор:
- читает `VirtualHome.json` (4 виртуальных устройства: свет, климат-сенсор, датчик движения, реле);
- подключается к RabbitMQ MQTT через **localhost:1883** (порт проброшен из контейнера);
- поднимает встроенный веб-UI на **http://localhost:5080**;
- говорит на Domovoy Native v1: announce каждого устройства (capabilities + state) + приём `/set` от сервера.

## 4. Три окна

| Окно | Что | URL / источник |
|---|---|---|
| 1 — UI сервера | список capability-устройств, контролы по capability, live по SignalR | http://localhost (`/devices`) |
| 2 — UI эмулятора | сенсорные значения (ползунки), состояние актуаторов, лог MQTT | http://localhost:5080 |
| 3 — Логи | сквозная диагностика | консоль `dotnet run` + `docker compose logs -f connectivity-service unified-device-service api-gateway db-gateway` |

## 5. Прогон сценария

**A. Управление с UI сервера → реакция в эмуляторе.**
1. На `/devices` дождитесь появления `Living Room Light` и `Garage Relay` (5–10 сек после старта эмулятора).
2. Переключите `on_off`, подвиньте `brightness` (0–100 %).
3. В UI эмулятора и логах видна команда (`DeviceCommandV1` → `Codec.Encode` → MQTT `/set`) и обновлённое состояние.

**B. Изменение датчика в эмуляторе → реакция в UI сервера.**
1. На `/devices` найдите `Kitchen Climate`.
2. В UI эмулятора подвиньте `temperature`/`humidity`/`co2`.
3. Значение в UI сервера обновится по SignalR (без F5).

**C. Сенсор присутствия.**
1. В UI эмулятора у `Hallway Motion` переключите `occupancy`.
2. В UI сервера значение обновится; в логах виден полный путь `state report → CapabilityDeviceManager → DeviceStateUpdatedEvent → EventRelayService → SignalR + EventInterceptor → Mongo`.

## 6. Полезные URL

| Сервис | URL |
|---|---|
| WebUI | http://localhost |
| ApiGateway (Swagger) | http://localhost:5000/swagger |
| DbGateway (OpenAPI) | http://localhost:5001/openapi |
| `GET /api/capability-devices` (прокси → DbGateway) | http://localhost:5000/api/capability-devices |
| SignalR hub | ws://localhost:5000/hub/devices |
| RabbitMQ Management | http://localhost:15672 (user/pass из `.env`) |
| MongoDB | mongodb://localhost:27017/domovoy |
| Prometheus | http://localhost:9090 |
| Mongo exporter | http://localhost:9216/metrics |
| Zigbee2MQTT UI | http://localhost:8081 (если поднят) |
| Эмулятор UI | http://localhost:5080 |

## 7. Полезные команды

```powershell
# логи нужных сервисов
docker compose logs -f api-gateway connectivity-service unified-device-service db-gateway

# пересборка одного сервиса после правки кода
docker compose up -d --build api-gateway

# остановка
docker compose down

# полная очистка (включая volume'ы Mongo/RabbitMQ/Prometheus)
docker compose down -v

# Mongo: посмотреть коллекцию capability-устройств
docker compose exec mongodb mongosh domovoy --eval "db.capability_devices.find().pretty()"
```

## 8. Траблшутинг

- **Контейнер `zigbee2mqtt` в рестарт-цикле** —
  - нет USB-стика: не запускайте его в команде (см. §2 — селективный список сервисов);
  - стик есть, но не проброшен: выполните `usbipd attach --wsl --busid <X-Y>` или запустите `.\start-domovoy.bat` (см. §2b);
  - `usbipd list` показывает «Not shared» — единожды `usbipd bind --busid <X-Y>` от админа;
  - стик пропал после ребута — это нормально, `attach` нужно повторять (или каждый раз запускать `start-domovoy.bat`);
  - Z2M пишет MQTT auth error — креды в `.env` не совпадают с `data/zigbee2mqtt/configuration.yaml` (см. §1).
- **Эмулятор не подключается к MQTT** — `docker compose ps rabbitmq` (поднят?), порт 1883 не занят локальным брокером, в `VirtualHome.json` `mqtt_broker: "localhost:1883"`.
- **На `/devices` пусто** — устройства появляются после announce от адаптера: эмулятор → `DomovoyNativeAdapter` → `Envelope<DeviceDiscoveredV1>` → `EventInterceptor` → `capability_devices`. Подождите 5–10 сек, затем `Refresh`.
- **Команды не доходят** — проверьте `api-gateway` (публикует `DeviceCommandV1`), `connectivity-service` (адаптер фильтрует «свои» по deterministic id, кодирует и шлёт MQTT).
- **WebUI не цепляет SignalR** — `VITE_API_BASE_URL` баковался во время сборки в `http://localhost:5000` (значение по умолчанию), что соответствует проброшенному порту ApiGateway. Если меняли — пересоберите `webui`.
- **Свежий старт после поломанного состояния** — `docker compose down -v` + перезапуск.
