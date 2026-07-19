/*
 * HouseDimmer.ino — диммер света дома: зал, обеденная зона, спальня (Domovoy Native v1).
 *
 * НЕ пример: боевая прошивка конкретного дома, перенесённая с ArduinoHA (Home Assistant)
 * на DomovoyClient. Логика локальная и работает без сервера: клик кнопки — вкл/выкл,
 * удержание — плавное диммирование (для диммируемых каналов). Домовой получает статусы
 * и может присылать команды on_off/brightness.
 *
 * Плата: Uno/Nano + Ethernet-модуль W5500 (библиотека Ethernet2, CS = 10).
 * Пин-карта:
 *   3        — детектор нуля фазовых диммеров (INT1, RISING)
 *   5, 6     — фазовые диммеры: зал, обеденная зона
 *   8        — реле: спальня (без диммирования)
 *   10       — CS W5500
 *   A0..A2   — кнопки: зал, обеденная зона, спальня
 *
 * Яркость наружу отдаётся в каноническом виде Домового 0..100 %; внутри остаётся
 * сырой диапазон диммера 0..250 с CRT-гаммой.
 */

// SRAM на Uno/Nano ~2 КБ — ужимаем лимиты библиотеки ДО include (см. DomovoyClient.h)
#define DOMOVOY_MAX_DEVICES 3
#define DOMOVOY_MAX_CAPS 2
#define DOMOVOY_CMD_DOC_SIZE 128
#define DOMOVOY_MQTT_BUFFER 192

#include <DomovoyClient.h>
#include <SPI.h>
#include <Ethernet2.h>
#include <EncButton.h>
#include <GyverDimmer.h>
#include <GyverTimers.h>

// ============================== СЕТЬ И СЕРВЕР ==============================

byte mac[] = {0xDE, 0xAD, 0xBE, 0xEF, 0xFE, 0x01};
IPAddress ip(192, 168, 1, 170);

const char *DOMOVOY_SERVER = "192.168.1.49"; // сервер Домового (RabbitMQ MQTT)
const uint16_t DOMOVOY_PORT = 1883;
const char *MQTT_USER = "domovoy";           // = RABBITMQ_DEFAULT_USER из .env docker-compose
const char *MQTT_PASS = "domovoy";           // = RABBITMQ_DEFAULT_PASS
const char *HUB_ID = "house-dimmer";         // MQTT client id платы (не логическое устройство)

EthernetClient ethClient;
DomovoyHub hub(ethClient);

unsigned long lastConnectAttempt = 0;
const unsigned long RECONNECT_INTERVAL = 10000;

// ============================== ДИММЕР И СВЕТ ==============================

// Гамма-таблица CRT: линейная команда яркости → воспринимаемая глазом яркость лампы
const uint8_t CRTgammaPGM[256] PROGMEM = {
  0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1,
  2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 5,
  5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 8, 8, 8, 8, 8,
  9, 10, 10, 10, 10, 11, 11, 12, 12, 12, 12, 13, 13, 13, 14, 14,
  15, 15, 15, 16, 17, 17, 17, 17, 18, 18, 19, 20, 20, 20, 20, 21,
  22, 22, 23, 23, 23, 24, 25, 25, 26, 26, 27, 27, 28, 28, 29, 30,
  30, 31, 31, 32, 33, 33, 34, 35, 35, 36, 37, 37, 38, 38, 39, 40,
  41, 41, 42, 43, 44, 45, 45, 46, 47, 47, 48, 49, 50, 51, 52, 53,
  54, 54, 55, 56, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67,
  68, 69, 69, 70, 71, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 84,
  85, 86, 87, 89, 90, 91, 93, 94, 95, 96, 97, 98, 100, 101, 102, 103,
  105, 106, 108, 109, 110, 111, 113, 115, 117, 118, 119, 121, 122, 123, 124, 127,
  128, 130, 131, 133, 134, 136, 137, 139, 140, 143, 145, 146, 147, 148, 151, 153,
  154, 156, 158, 159, 162, 163, 165, 167, 169, 171, 173, 174, 176, 179, 180, 182,
  185, 186, 188, 190, 192, 194, 196, 199, 201, 202, 205, 207, 209, 211, 214, 216,
  218, 220, 223, 225, 226, 230, 231, 235, 236, 240, 241, 245, 246, 249, 249, 249
};

byte getBrightCRT(byte val) {
  return pgm_read_byte(&(CRTgammaPGM[val]));
}

const uint8_t LIGHT_COUNT = 3;
const uint8_t LightPins[LIGHT_COUNT] = {5, 6, 8}; // 5,6 — диммеры; 8 — реле спальни

DomovoyDevice hallLight("hall-light", "Зал свет", "House Dimmer", "1.0");
DomovoyDevice dinnerLight("dinner-light", "Обеденный свет", "House Dimmer", "1.0");
DomovoyDevice bedroomLight("bedroom-light", "Спальня свет", "House Dimmer", "1.0");
DomovoyDevice *lightDevs[LIGHT_COUNT] = {&hallLight, &dinnerLight, &bedroomLight};

DimmerMulti<LIGHT_COUNT> dim; // каналы привязываются только для диммируемых ламп
Button dimBtn[LIGHT_COUNT];

struct DimmerLight {
  bool dimmUp;        // направление следующего диммирования удержанием
  uint8_t val;        // сырая яркость 0..250 (0 = выключено)
  uint8_t pin;
  unsigned long timing;
  bool dimmable;
};

const uint8_t dimStep = 2;

DimmerLight lamps[LIGHT_COUNT];

// ============================== ПУБЛИКАЦИЯ И КОМАНДЫ ==============================

void applyLight(uint8_t i) {
  if (lamps[i].dimmable) dim.write(i, getBrightCRT(lamps[i].val));
  else digitalWrite(lamps[i].pin, lamps[i].val > 0);
}

void publishLight(uint8_t i) {
  StaticJsonDocument<64> doc;
  doc["on_off"] = lamps[i].val > 0;
  if (lamps[i].dimmable) doc["brightness"] = map(lamps[i].val, 0, 250, 0, 100);
  hub.publishState(*lightDevs[i], doc);
}

void publishAllStates() {
  for (uint8_t i = 0; i < LIGHT_COUNT; i++) publishLight(i);
}

void handleLightSet(uint8_t i, JsonObject set) {
  DimmerLight &l = lamps[i];
  bool changed = false;

  if (l.dimmable && set.containsKey("brightness")) {
    uint8_t percent = constrain((int)set["brightness"].as<float>(), 0, 100);
    l.val = map(percent, 0, 100, 0, 250);
    applyLight(i);
    changed = true;
  }

  if (set.containsKey("on_off")) {
    bool state = set["on_off"].as<bool>();
    if (state && l.val == 0) {
      l.val = 250;
      applyLight(i);
      changed = true;
    } else if (!state && l.val > 0) {
      l.val = 0;
      applyLight(i);
      changed = true;
    }
  }

  if (changed) {
    Serial.print(F("Set light "));
    Serial.print(i);
    Serial.print(F(" val "));
    Serial.println(l.val);

    publishLight(i);
  }
}

void onHallLight(JsonObject set) { handleLightSet(0, set); }
void onDinnerLight(JsonObject set) { handleLightSet(1, set); }
void onBedroomLight(JsonObject set) { handleLightSet(2, set); }

// ============================== ПРЕРЫВАНИЯ ДИММЕРА ==============================

// прерывание детектора нуля
void isr() {
  dim.tickZero();
  Timer2.restart();
}

// прерывание таймера
ISR(TIMER2_A) {
  dim.tickTimer();
}

// ============================== ИНИЦИАЛИЗАЦИЯ (SETUP) ==============================

bool connectDomovoy() {
  lastConnectAttempt = millis();
  if (!hub.connect(HUB_ID)) return false;
  Serial.println(F("Domovoy connected"));
  publishAllStates();
  return true;
}

void setup() {
  Ethernet.init(10); // CS модуля W5500

  delay(1000);
  Ethernet.begin(mac, ip);

  Serial.begin(9600);
  Serial.println(Ethernet.localIP());

  attachInterrupt(1, isr, RISING); // INT1 = D3 — детектор нуля
  Timer2.enableISR();
  Timer2.setPeriod(dim.getPeriod()); // период в мкс (37 us для сети 50 Гц)

  for (uint8_t i = 0; i < LIGHT_COUNT; i++) {
    dimBtn[i].init(A0 + i, INPUT_PULLUP);
    dimBtn[i].setHoldTimeout(500);

    lamps[i].dimmUp = true;
    lamps[i].val = 0;
    lamps[i].timing = 0;
    lamps[i].dimmable = true;
    lamps[i].pin = LightPins[i];
  }
  lamps[2].dimmable = false; // спальня — реле, без диммирования

  for (uint8_t i = 0; i < LIGHT_COUNT; i++) {
    if (lamps[i].dimmable) dim.attach(i, lamps[i].pin);
    else pinMode(lamps[i].pin, OUTPUT);
  }

  for (uint8_t i = 0; i < LIGHT_COUNT; i++) {
    lightDevs[i]->addBoolean("on_off", /*writable*/ true);
    if (lamps[i].dimmable) lightDevs[i]->addNumber("brightness", "%", 0, 100, /*writable*/ true);
    hub.addDevice(*lightDevs[i]);
  }
  hallLight.onCommand(onHallLight);
  dinnerLight.onCommand(onDinnerLight);
  bedroomLight.onCommand(onBedroomLight);

  hub.setServer(DOMOVOY_SERVER, DOMOVOY_PORT);
  hub.setCredentials(MQTT_USER, MQTT_PASS);
  connectDomovoy();

  Serial.println(F("Started..."));
}

// ============================== ОСНОВНОЙ ЦИКЛ (LOOP) ==============================

// Держим сессию с Домовым; при обрыве переподключаемся не чаще RECONNECT_INTERVAL
// (неудачная попытка блокирует loop на TCP-таймаут — кнопки в этот момент могут
// отозваться с задержкой, как и в HA-версии). После коннекта connect() сам
// переобъявляет устройства, нам остаётся дослать снимок состояний.
void maintainDomovoy() {
  if (hub.connected()) {
    hub.loop();
    return;
  }
  if (millis() - lastConnectAttempt < RECONNECT_INTERVAL) return;
  connectDomovoy();
}

void loop() {
  // Ethernet.maintain() намеренно НЕ вызывается: при статическом IP это no-op,
  // а его вызов тянет весь DHCP-код Ethernet2 — лишние ~2 КБ флеша, которых на Nano нет
  maintainDomovoy();

  for (uint8_t i = 0; i < LIGHT_COUNT; i++) {
    dimBtn[i].tick();
  }

  for (uint8_t i = 0; i < LIGHT_COUNT; i++) {
    bool sendData = false;

    // Отпускание после удержания: фиксируем новое направление диммирования и шлём статус
    if (dimBtn[i].release() && lamps[i].dimmable) {
      bool dir = lamps[i].dimmUp;
      lamps[i].dimmUp = !dir;
      lamps[i].timing = 0;
      sendData = true;
    }

    // Клик — вкл/выкл
    if (dimBtn[i].click()) {
      if (lamps[i].val > 0) {
        lamps[i].val = 0;
        lamps[i].dimmUp = true;
      } else {
        lamps[i].val = 250;
        lamps[i].dimmUp = false;
      }

      applyLight(i);

      Serial.print(F("Click light "));
      Serial.print(i);
      Serial.print(F(" val "));
      Serial.println(lamps[i].val);
      sendData = true;
    }

    // Удержание — плавное диммирование в текущем направлении
    if (dimBtn[i].holding() && lamps[i].dimmable) {
      bool timeTick = false;

      if (lamps[i].timing == 0 || millis() - lamps[i].timing > 12) timeTick = true;

      if (lamps[i].dimmUp && lamps[i].val < 250 && timeTick) {
        lamps[i].val += dimStep;
        lamps[i].timing = millis();

        dim.write(i, getBrightCRT(lamps[i].val));
      }
      if (!lamps[i].dimmUp && lamps[i].val > 0 && timeTick) {
        lamps[i].val -= dimStep;
        lamps[i].timing = millis();

        dim.write(i, getBrightCRT(lamps[i].val));
      }
    }

    if (sendData) {
      publishLight(i);

      Serial.print(F("Sent light "));
      Serial.print(i);
      Serial.print(F(" val "));
      Serial.println(lamps[i].val);
    }
  }
}
