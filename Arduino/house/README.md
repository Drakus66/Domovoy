# Прошивки дома (не примеры)

Боевые прошивки Arduino-плат конкретного дома, перенесённые с ArduinoHA (Home Assistant)
на Domovoy Native protocol v1 (библиотека [`../libraries/DomovoyClient`](../libraries/DomovoyClient)).

Принцип: **вся логика — на борту**. Кнопки переключают реле напрямую, диммирование,
программный ШИМ конвекторов, разгон вентиляторов и калибровка CO2 считаются локально.
Если сервер выключен или сеть отвалилась, дом продолжает работать; Домовой лишь
получает статусы и может присылать команды.

Каждая плата держит одну MQTT-сессию (hub id), но выступает мостом для нескольких
логических устройств: у каждого свой `deviceId`, набор capability и топики
`domovoy/native/<deviceId>/{announce,state,set,availability}`. Доступность платы —
`domovoy/hub/<hubId>/status` (LWT).

## HouseMega — Arduino Mega 2560 + W5100 (192.168.1.180, hub `house-mega`)

| deviceId | Что это | Capability | Пины |
|---|---|---|---|
| `hallway-light` / `kitchen-light` / `terrace-light` | потолочный свет | `on_off` (rw) | реле 44/45/46, кнопки A0/A1/A2 |
| `bathroom-switch` / `bathroom-mirror-switch` | виртуальные выключатели санузла | `on_off` (rw) | кнопки A3/A4 |
| `yard-switch` | выключатель прожектора во дворе | `on_off` (rw) | кнопка A5 (одиночный клик) |
| `hall-climate` / `bedroom-climate` | климат комнат | `temperature`, `humidity`, `co2` | DHT22 на 22/23, MH-Z19 на Serial2/Serial1 |
| `hall-heater` … `bathroom-heater`, `inlet-air-heater` | конвекторы и нагрев притока | `valve` 0..100 % (rw) | реле 35..39 |
| `inlet-fan` / `exhaust-fan` | вентиляторы приток/вытяжка | `fan_speed` 0..100 % (rw) | фазовые диммеры 4/5, детектор нуля 3 |
| `water-pressure` | реле давления воды | `contact` | A6 |
| `co2-calibration` | ручная калибровка MH-Z19 | `on_off` (rw) | — |

Виртуальные выключатели не несут нагрузки на плате — их состояние слушают правила
Домового и включают свет на других устройствах (как это делали автоматизации HA).

## HouseDimmer — Uno/Nano + W5500 Ethernet2 (192.168.1.170, hub `house-dimmer`)

| deviceId | Что это | Capability | Пины |
|---|---|---|---|
| `hall-light` | зал, фазовый диммер | `on_off`, `brightness` 0..100 % (rw) | диммер 5, кнопка A0 |
| `dinner-light` | обеденная зона, фазовый диммер | `on_off`, `brightness` 0..100 % (rw) | диммер 6, кнопка A1 |
| `bedroom-light` | спальня, реле | `on_off` (rw) | реле 8, кнопка A2 |

Клик кнопки — вкл/выкл, удержание — плавное диммирование (детектор нуля на D3, CS W5500 = 10).

## Настройка перед прошивкой

В шапке каждого скетча:

- `DOMOVOY_SERVER` / `DOMOVOY_PORT` — адрес сервера Домового (RabbitMQ MQTT, порт 1883);
- `MQTT_USER` / `MQTT_PASS` — значения `RABBITMQ_DEFAULT_USER` / `RABBITMQ_DEFAULT_PASS`
  из `.env` рядом с `docker-compose.yml`;
- `mac` / `ip` — сетевые адреса самой платы. Первый октет MAC должен быть чётным
  (unicast + locally-administered, например `0x02`), иначе коммутатор не выучит адрес.

## Зависимости

- `DomovoyClient` (из [`../libraries`](../libraries)) + ArduinoJson + PubSubClient;
- EncButton, GyverDimmer, GyverTimers;
- HouseMega: TimerMs, DHT sensor library, MH-Z19, Ethernet (W5100);
- HouseDimmer: Ethernet2 (W5500).

## Отличия от HA-версий

- ArduinoHA-сущности одной «мега-железки» разложены на самостоятельные логические
  устройства с каноническими capability Домового;
- яркость диммера наружу — 0..100 % (внутри остался сырой диапазон 0..250 с CRT-гаммой),
  мощность конвекторов — `valve` 0..100 %;
- добавлено автопереподключение к брокеру с переобъявлением устройств и досылкой
  снимка состояний;
- исправлено: смена скорости вентилятора во время разгона больше не откатывается
  таймером на прежнюю цель;
- убраны мёртвые куски HA-версий: неопрашиваемый DS18B20 (пин 31, зарезервирован),
  четвёртая кнопка диммера, а также легаси двойного клика A5 — теперь A5 включает
  прожектор одиночным кликом (виртуальный выключатель террасы удалён, свет террасы
  включает своя кнопка A2);
- в HouseDimmer убран `Ethernet.maintain()`: при статическом IP это no-op, а его вызов
  тянет DHCP-код Ethernet2 (~2 КБ флеша).

## Бюджет ресурсов (проверено сборкой PlatformIO)

- HouseMega (Mega 2560): Flash ~18 %, RAM ~48 %;
- HouseDimmer (Nano 328p): **Flash ~92 %**, RAM ~51 % — запас на Nano мал, новый код
  добавлять почти некуда (сборка обязательно с LTO — Arduino IDE включает его сам).
