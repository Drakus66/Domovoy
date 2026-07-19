# Подключение плат ESPHome (ESP32/ESP8266) — Эпик 2J

Domovoy обнаруживает и управляет платами ESP32/ESP8266 с прошивкой [ESPHome](https://esphome.io)
через **MQTT** по протоколу Home Assistant MQTT Discovery. Свою прошивку писать не нужно — адаптер
[`EspHomeMqttAdapter`](../src/Services/Domovoy.Connectivity/Adapters/EspHomeMqttAdapter.cs) поднимает
устройство по общему capability-пути: оно появляется на `/devices`, копит историю/телеметрию,
участвует в правилах, блоках и ML — как любое другое устройство.

> Альтернатива: прошить плату нативным `DomovoyClient` (ESP32 Arduino-совместим) — тогда работает
> путь `DomovoyNativeAdapter` **без изменений на сервере**. См. `docs/architecture/roadmap.md`.
>
> Какую плату и датчики покупать — см. [рекомендуемые устройства](recommended_devices_ru.md)
> (платы ESP32 с Ethernet+PoE, датчики CO₂).

## Что нужно

- Тот же брокер, что и у всего стека — **MQTT-плагин RabbitMQ** (порт 1883). Логин/пароль — как у сервисов.
- ESPHome с включённым компонентом `mqtt:` и `discovery: true`.

## Конвенция топиков

Адаптер статически слушает `homeassistant/#` (discovery) и `domovoy/esphome/#` (состояние/команды).
Задайте `topic_prefix: domovoy/esphome/<имя-платы>`, чтобы всё попало под эту конвенцию. Топики за её
пределами тоже работают — они выучиваются из discovery и подписываются динамически.

## Пример `board.yaml`

```yaml
esphome:
  name: esp1

esp32:
  board: esp32dev

# Тот же брокер, что и весь стек (MQTT-плагин RabbitMQ)
mqtt:
  broker: 192.168.1.10        # адрес RabbitMQ
  port: 1883
  username: domovoy
  password: !secret mqtt_password
  topic_prefix: domovoy/esphome/esp1
  discovery: true             # публикует retained-конфиги в homeassistant/…

# Датчик температуры → capability `temperature`
sensor:
  - platform: dht
    pin: GPIO4
    temperature:
      name: "Living Temp"
      device_class: temperature
    humidity:
      name: "Living Humidity"
      device_class: humidity

# Реле → capability `on_off` (исполняет команды)
switch:
  - platform: gpio
    pin: GPIO5
    name: "Relay"

# Числовая уставка → writable numeric (temp → temperature_setpoint)
number:
  - platform: template
    name: "Target Temp"
    device_class: temperature
    min_value: 5
    max_value: 30
    step: 0.5
    optimistic: true
```

После прошивки (OTA/USB) плата сама опубликует discovery — устройство появится на `/devices`.
Архетип назначит классификатор 2D по набору capability + `adapterSource=EspHome`. Падение платы
(Last-Will на `<prefix>/status`) переводит её сущности в offline.

## Поддержано в v1

`sensor` (temperature/humidity/carbon_dioxide/illuminance/power/energy/battery по `device_class`),
`binary_sensor` (motion/occupancy/presence → `occupancy`; door/window → `contact`), `switch`, `light`
(on/off + brightness), `number`, `select`, `lock`. Неизвестные величины получают кастомный
capability id из `object_id`.

**Пока не поддержано:** `cover`/`climate`/`fan` (многотопиковые сущности), JSON-схема света.

## Известное ограничение

RabbitMQ MQTT не доставляет retained-сообщения на wildcard-подписки, поэтому после рестарта сервиса
`Connectivity` retained-конфиги discovery могут не прийти сразу. ESPHome переопубликовывает discovery на
своём реконнекте (и шлёт birth-сообщение), так что устройство восстановится; команды на уже известные
топики работают в любом случае.
