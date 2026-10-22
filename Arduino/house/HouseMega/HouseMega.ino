/*
 * HouseMega.ino — центральная Arduino Mega 2560 дома (Domovoy Native protocol v1).
 *
 * НЕ пример: боевая прошивка конкретного дома, перенесённая с ArduinoHA (Home Assistant)
 * на DomovoyClient. Вся логика живёт на борту: кнопки переключают реле напрямую, ШИМ
 * конвекторов и разгон вентиляторов считаются локально — при недоступном сервере дом
 * продолжает работать; Домовой лишь получает статусы и может присылать команды.
 *
 * Плата держит одну MQTT-сессию (hub id "house-mega"), но выступает мостом для
 * НЕСКОЛЬКИХ логических устройств (как Zigbee-мост): каждый светильник/датчик/конвектор —
 * отдельный DomovoyDevice со своими capability и топиками domovoy/native/<deviceId>/...
 *
 * Пин-карта:
 *   3        — детектор нуля фазовых диммеров вентиляторов (INT, RISING)
 *   4, 5     — фазовые диммеры вентиляторов (приток / вытяжка)
 *   22, 23   — DHT22 (зал / спальня)
 *   31       — зарезервирован под шину DS18B20 (в HA-версии объявлялась, но не опрашивалась)
 *   35..39   — реле конвекторов: зал, кухня, спальня, ванная, нагрев приточного воздуха
 *   44..46   — реле потолочного света: коридор, кухня, терраса
 *   53       — SS аппаратного SPI (W5100), обязан быть OUTPUT
 *   A0..A2   — кнопки потолочного света (коридор, кухня, терраса)
 *   A3, A4   — кнопки санузла и зеркала санузла (виртуальные выключатели)
 *   A5       — кнопка прожектора во дворе (одиночный клик)
 *   A6       — реле давления воды
 *   Serial2  — MH-Z19 зал (TX→17, RX→16), Serial1 — MH-Z19 спальня (TX→19, RX→18)
 */

// Лимиты библиотеки задаются ДО include (см. DomovoyClient.h): плата ведёт 17 логических
// устройств, максимум capability у одного — 3 (климат: temperature + humidity + co2).
#define DOMOVOY_MAX_DEVICES 17
#define DOMOVOY_MAX_CAPS 3

#include <DomovoyClient.h>
#include <SPI.h>
#include <Ethernet.h>
#include <EncButton.h>
#include <DHT.h>
#include <MHZ19.h>
#include <TimerMs.h>
#include <GyverTimers.h>
#include <GyverDimmer.h>

// ============================== СЕТЬ И СЕРВЕР ==============================

// Первый октет 0x02 = unicast + locally-administered (чётный!). НЕЛЬЗЯ нечётный первый
// октет (напр. 0x01) — это multicast-бит, коммутатор не учит такой MAC → плата выпадает из сети.
byte mac[] = {0x02, 0x02, 0x03, 0x04, 0x05, 0x11};
IPAddress ip(192, 168, 1, 180);

const char *DOMOVOY_SERVER = "192.168.1.49"; // сервер Домового (RabbitMQ MQTT)
const uint16_t DOMOVOY_PORT = 1883;
const char *MQTT_USER = "domovoy";           // = RABBITMQ_DEFAULT_USER из .env docker-compose
const char *MQTT_PASS = "domovoy";           // = RABBITMQ_DEFAULT_PASS
const char *HUB_ID = "house-mega";           // MQTT client id платы (не логическое устройство)

EthernetClient ethClient;
DomovoyHub hub(ethClient);

unsigned long lastConnectAttempt = 0;
const unsigned long RECONNECT_INTERVAL = 10000; // попытка коннекта блокирует loop на время
                                                // TCP-таймаута — не чаще раза в 10 с

// ============================== ЛОГИЧЕСКИЕ УСТРОЙСТВА ==============================

// Потолочный свет — реле 44..46, локальные кнопки A0..A2
const uint8_t LIGHT_COUNT = 3;
const uint8_t LIGHT_RELAY_BASE = 44;
DomovoyDevice hallwayLight("hallway-light", "Коридор свет", "House Mega", "1.0");
DomovoyDevice kitchenLight("kitchen-light", "Кухня свет", "House Mega", "1.0");
DomovoyDevice terraceLight("terrace-light", "Терраса свет", "House Mega", "1.0");
DomovoyDevice *lights[LIGHT_COUNT] = {&hallwayLight, &kitchenLight, &terraceLight};
uint8_t lightStates[LIGHT_COUNT];

// Виртуальные выключатели: нагрузки на плате нет — их состояние слушают правила Домового
// (свет санузла/зеркала/прожектора висит на других устройствах).
const uint8_t SWITCH_COUNT = 3;
DomovoyDevice bathroomSwitch("bathroom-switch", "Выключатель санузла", "House Mega", "1.0");
DomovoyDevice bathroomMirrorSwitch("bathroom-mirror-switch", "Выключатель зеркала санузла", "House Mega", "1.0");
DomovoyDevice yardSwitch("yard-switch", "Выключатель прожектора двора", "House Mega", "1.0");
DomovoyDevice *virtualSwitches[SWITCH_COUNT] = {&bathroomSwitch, &bathroomMirrorSwitch, &yardSwitch};
uint8_t switchStates[SWITCH_COUNT];

// Климат: DHT22 + MH-Z19 на комнату
DomovoyDevice hallClimate("hall-climate", "Зал климат", "House Mega", "1.0");
DomovoyDevice bedroomClimate("bedroom-climate", "Спальня климат", "House Mega", "1.0");

// Конвекторы/нагреватели: команда — valve 0..100 %, на борту — окно программного ШИМ
struct HeaterData {
  uint8_t percent; // последняя команда 0..100 — эхо в состояние valve
  uint8_t value;   // масштаб окна ШИМ (0..150 конвекторы, 0..250 нагрев воздуха)
  int pin;
  bool state;
  DomovoyDevice *dev;
};
DomovoyDevice hallHeater("hall-heater", "Зал конвектор", "House Mega", "1.0");
DomovoyDevice kitchenHeater("kitchen-heater", "Кухня конвектор", "House Mega", "1.0");
DomovoyDevice bedroomHeater("bedroom-heater", "Спальня конвектор", "House Mega", "1.0");
DomovoyDevice bathroomHeater("bathroom-heater", "Ванная конвектор", "House Mega", "1.0");
DomovoyDevice inletAirHeater("inlet-air-heater", "Нагрев приточного воздуха", "House Mega", "1.0");
HeaterData hallHeatData, kitchenHeatData, bedroomHeatData, bathroomHeatData, airHeatData;

// Вентиляторы приток/вытяжка — фазовые диммеры на пинах 4,5
struct FanData {
  uint8_t speed;        // текущая скорость 0..100 (на время разгона — 90)
  uint8_t reported;     // последняя заданная скорость — эхо в состояние fan_speed
  uint8_t startToSpeed; // целевая скорость после разгона
  TimerMs *startTimer;
  DomovoyDevice *dev;
};
DomovoyDevice inletFan("inlet-fan", "Приточный вентилятор", "House Mega", "1.0");
DomovoyDevice exhaustFan("exhaust-fan", "Вытяжной вентилятор", "House Mega", "1.0");
FanData fans[2];
DimmerMulti<2> fanDimmers; // каналы 0,1 → пины 4,5
const uint8_t fanMinSpeed = 50;

// Реле давления воды (A6 — отдельный пин, чтобы не конфликтовать с кнопкой террасы A5)
DomovoyDevice waterPressure("water-pressure", "Реле давления воды", "House Mega", "1.0");
Button waterSensorButton;
bool waterState = false;

// Калибровка CO2 — writable-тумблер: ВКЛ запускает выдержку на проветривание,
// по её окончании — принудительная калибровка нуля обоих датчиков
DomovoyDevice co2Calibration("co2-calibration", "Калибровка датчиков CO2", "House Mega", "1.0");

// ============================== ДАТЧИКИ И ТАЙМЕРЫ ==============================

DHT dht1(22, DHT22); // зал
DHT dht2(23, DHT22); // спальня

MHZ19 co2_1; // зал (Serial2)
MHZ19 co2_2; // спальня (Serial1)
bool co2Calibrating;

Button dimBtn[6]; // A0..A5

TimerMs sensorGetTimer(20000, 1, 0);
TimerMs convTimer1(20000, 1, 0);      // период программного ШИМ конвекторов
TimerMs airHeaterTimer(5000, 1, 0);   // период ШИМ нагрева приточного воздуха
TimerMs calibrateTimer(20 * 60000, 0, 1); // выдержка ~20 мин перед калибровкой CO2

// ============================== ПУБЛИКАЦИЯ СОСТОЯНИЙ ==============================

void publishBool(DomovoyDevice &d, const char *cap, bool v) {
  StaticJsonDocument<64> doc;
  doc[cap] = v;
  hub.publishState(d, doc);
}

void publishNumber(DomovoyDevice &d, const char *cap, float v) {
  StaticJsonDocument<64> doc;
  doc[cap] = v;
  hub.publishState(d, doc);
}

void publishClimate(DomovoyDevice &d, float t, float h) {
  StaticJsonDocument<96> doc;
  doc["temperature"] = t;
  doc["humidity"] = h;
  hub.publishState(d, doc);
}

// Полный снимок всего, чем владеет плата, — после (пере)подключения. Климат не шлём:
// его опубликует ближайший опрос датчиков (sensorGetTimer, каждые 20 с).
void publishAllStates() {
  for (uint8_t i = 0; i < LIGHT_COUNT; i++) publishBool(*lights[i], "on_off", lightStates[i]);
  for (uint8_t i = 0; i < SWITCH_COUNT; i++) publishBool(*virtualSwitches[i], "on_off", switchStates[i]);

  publishNumber(hallHeater, "valve", hallHeatData.percent);
  publishNumber(kitchenHeater, "valve", kitchenHeatData.percent);
  publishNumber(bedroomHeater, "valve", bedroomHeatData.percent);
  publishNumber(bathroomHeater, "valve", bathroomHeatData.percent);
  publishNumber(inletAirHeater, "valve", airHeatData.percent);

  for (uint8_t i = 0; i < 2; i++) publishNumber(*fans[i].dev, "fan_speed", fans[i].reported);

  publishBool(waterPressure, "contact", waterState);
  publishBool(co2Calibration, "on_off", co2Calibrating);
}

// ============================== ОБРАБОТЧИКИ КОМАНД ==============================

void handleLightSet(uint8_t i, JsonObject set) {
  if (!set.containsKey("on_off")) return;
  lightStates[i] = set["on_off"].as<bool>() ? 1 : 0;
  digitalWrite(LIGHT_RELAY_BASE + i, lightStates[i]);

  Serial.print(F("Light "));
  Serial.print(i);
  Serial.print(F(" state "));
  Serial.println(lightStates[i]);

  publishBool(*lights[i], "on_off", lightStates[i]);
}

void onHallwayLight(JsonObject set) { handleLightSet(0, set); }
void onKitchenLight(JsonObject set) { handleLightSet(1, set); }
void onTerraceLight(JsonObject set) { handleLightSet(2, set); }

void handleVirtualSwitchSet(uint8_t i, JsonObject set) {
  if (!set.containsKey("on_off")) return;
  switchStates[i] = set["on_off"].as<bool>() ? 1 : 0;
  publishBool(*virtualSwitches[i], "on_off", switchStates[i]);
}

void onBathroomSwitch(JsonObject set) { handleVirtualSwitchSet(0, set); }
void onBathroomMirrorSwitch(JsonObject set) { handleVirtualSwitchSet(1, set); }
void onYardSwitch(JsonObject set) { handleVirtualSwitchSet(2, set); }

void handleHeaterSet(HeaterData &h, JsonObject set, uint8_t scaleMax) {
  if (!set.containsKey("valve")) return;
  uint8_t percent = constrain((int)set["valve"].as<float>(), 0, 100);
  h.percent = percent;
  h.value = map(percent, 0, 100, 0, scaleMax);

  Serial.print(F("Heater => "));
  Serial.println(h.value);

  publishNumber(*h.dev, "valve", percent);
}

void onHallHeater(JsonObject set) { handleHeaterSet(hallHeatData, set, 150); }
void onKitchenHeater(JsonObject set) { handleHeaterSet(kitchenHeatData, set, 150); }
void onBedroomHeater(JsonObject set) { handleHeaterSet(bedroomHeatData, set, 150); }
void onBathroomHeater(JsonObject set) { handleHeaterSet(bathroomHeatData, set, 150); }
void onInletAirHeater(JsonObject set) { handleHeaterSet(airHeatData, set, 250); }

void handleFanSet(uint8_t i, JsonObject set) {
  if (!set.containsKey("fan_speed")) return;
  uint8_t setSpeed = constrain((int)set["fan_speed"].as<float>(), 0, 100);
  if (setSpeed < 35) setSpeed = 0; // ниже 35% мотор не крутится — трактуем как «выключить»

  Serial.print(F("Fan "));
  Serial.print(i);
  Serial.print(F(" speed "));
  Serial.println(setSpeed);

  if (setSpeed == 0) {
    fanDimmers.write(i, 0);
    fans[i].startTimer->stop();
    fans[i].startToSpeed = 0;
    fans[i].speed = 0;
    fans[i].reported = 0;
    publishNumber(*fans[i].dev, "fan_speed", 0);
    return;
  }

  if (setSpeed <= fanMinSpeed && fans[i].speed < 20) {
    // с малой скорости мотор не стартует: разгон на 90% и через 4 с спуск до цели
    fans[i].startTimer->start();
    fans[i].startToSpeed = setSpeed;
    fans[i].speed = 90;
  } else {
    if (setSpeed > 95) setSpeed = 95;
    fans[i].speed = setSpeed;
    fans[i].startToSpeed = 0;  // отменяем возможный незавершённый разгон,
    fans[i].startTimer->stop(); // иначе таймер вернул бы старую цель
  }
  fanDimmers.write(i, map(fans[i].speed, 0, 100, 0, 250));
  fans[i].reported = setSpeed;
  publishNumber(*fans[i].dev, "fan_speed", setSpeed);
}

void onInletFan(JsonObject set) { handleFanSet(0, set); }
void onExhaustFan(JsonObject set) { handleFanSet(1, set); }

// Команда калибровки CO2. ВКЛ — запускаем выдержку на проветривание, по её окончании
// произойдёт принудительная калибровка (см. handleCO2Calibration). ВЫКЛ — отмена.
// ABC остаётся выключенной в обоих случаях.
void onCalibrationSet(JsonObject set) {
  if (!set.containsKey("on_off")) return;
  bool state = set["on_off"].as<bool>();

  if (state) {
    co2_1.autoCalibration(false); // на всякий случай гарантируем, что ABC выключена
    co2_2.autoCalibration(false);
    co2Calibrating = true;
    calibrateTimer.start(); // выдержка ~20 мин, чтобы комната проветрилась до ~400 ppm
  } else {
    co2Calibrating = false;
    calibrateTimer.stop();
  }

  publishBool(co2Calibration, "on_off", state);
}

// ============================== ПРЕРЫВАНИЯ ДИММЕРА ==============================

// прерывание детектора нуля
void isr() {
  fanDimmers.tickZero();
  Timer2.restart();
}

// прерывание таймера
ISR(TIMER2_A) {
  fanDimmers.tickTimer();
}

// ============================== ИНИЦИАЛИЗАЦИЯ (SETUP) ==============================

void setupNetwork() {
  // На Arduino Mega аппаратный SPI висит на 50..53. Pin 53 (SS) при работе SPI-мастером
  // ОБЯЗАН быть OUTPUT, иначе AVR может уйти в режим slave и W5100 «отвалится» от сети.
  pinMode(53, OUTPUT);

  Ethernet.begin(mac, ip);
  Serial.begin(9600);
}

// Прерывания фазового диммера вентиляторов: детектор нуля (pin 3) + Timer2
void setupDimmerInterrupts() {
  attachInterrupt(digitalPinToInterrupt(3), isr, RISING);
  Timer2.enableISR();
  Timer2.setPeriod(fanDimmers.getPeriod()); // период в мкс (37 us для сети 50 Гц)
}

// Диагностика связи с датчиком CO2 при старте: версия прошивки + пробное чтение
// с кодом ошибки — видно, отвечает ли датчик по своему UART-порту.
void printCo2Diag(MHZ19 &sensor, const char *label) {
  char version[5] = {0};
  sensor.getVersion(version);

  int co2 = sensor.getCO2();

  Serial.print(label);
  Serial.print(F(" => version: "));
  Serial.print(version);
  Serial.print(F(", probe CO2: "));
  Serial.print(co2);
  Serial.print(F(", errorCode: "));
  Serial.println(sensor.errorCode); // RESULT_OK(1) = есть связь; TIMEOUT(2) = порт/проводка молчит
}

// Датчики CO2 (MH-Z19): зал на Serial2, спальня на Serial1
void setupCO2() {
  // ABC (автокалибровка) ВЫКЛЮЧЕНА намеренно: в жилых/спальных комнатах CO2 редко падает
  // до 400 ppm, и ABC занижала бы показания. Калибровка — только вручную по команде.
  Serial2.begin(9600); // зал: sensor TX -> 17 (Mega RX2), sensor RX -> 16 (Mega TX2)
  co2_1.begin(Serial2);
  co2_1.setRange(2000);
  co2_1.autoCalibration(false);
  printCo2Diag(co2_1, "Hall CO2 (Serial2)");

  Serial1.begin(9600); // спальня: sensor TX -> 19 (Mega RX1), sensor RX -> 18 (Mega TX1)
  co2_2.begin(Serial1);
  co2_2.setRange(2000);
  co2_2.autoCalibration(false);
  printCo2Diag(co2_2, "Bedroom CO2 (Serial1)");

  co2Calibrating = false;
}

void setupDHT() {
  dht1.begin(); // yellow -> vcc; red -> gnd; white -> 22
  dht2.begin(); // yellow -> vcc; red -> gnd; white -> 23
}

void setupButtons() {
  for (uint8_t i = 0; i < 6; i++) {
    dimBtn[i].init(A0 + i, INPUT_PULLUP);
    dimBtn[i].setHoldTimeout(500);
  }
  waterSensorButton.init(A6, INPUT_PULLUP);
}

void setupHeater(HeaterData &h, DomovoyDevice &dev, DeviceCommandCallback cb, int pin) {
  h.dev = &dev;
  h.pin = pin;
  h.percent = 0;
  h.value = 0;
  h.state = false;
  pinMode(pin, OUTPUT);

  dev.addNumber("valve", "%", 0, 100, /*writable*/ true);
  dev.onCommand(cb);
  hub.addDevice(dev);
}

// Объявление capability всех логических устройств и их регистрация на плате-мосте
void setupDevices() {
  for (uint8_t i = 0; i < LIGHT_COUNT; i++) {
    lightStates[i] = 0;
    pinMode(LIGHT_RELAY_BASE + i, OUTPUT);
    lights[i]->addBoolean("on_off", /*writable*/ true);
    hub.addDevice(*lights[i]);
  }
  hallwayLight.onCommand(onHallwayLight);
  kitchenLight.onCommand(onKitchenLight);
  terraceLight.onCommand(onTerraceLight);

  for (uint8_t i = 0; i < SWITCH_COUNT; i++) {
    switchStates[i] = 0;
    virtualSwitches[i]->addBoolean("on_off", /*writable*/ true);
    hub.addDevice(*virtualSwitches[i]);
  }
  bathroomSwitch.onCommand(onBathroomSwitch);
  bathroomMirrorSwitch.onCommand(onBathroomMirrorSwitch);
  yardSwitch.onCommand(onYardSwitch);

  hallClimate.addNumber("temperature", "°C", -20, 50);
  hallClimate.addNumber("humidity", "%", 0, 100);
  hallClimate.addNumber("co2", "ppm", 0, 2000);
  hub.addDevice(hallClimate);

  bedroomClimate.addNumber("temperature", "°C", -20, 50);
  bedroomClimate.addNumber("humidity", "%", 0, 100);
  bedroomClimate.addNumber("co2", "ppm", 0, 2000);
  hub.addDevice(bedroomClimate);

  setupHeater(hallHeatData, hallHeater, onHallHeater, 35);
  setupHeater(kitchenHeatData, kitchenHeater, onKitchenHeater, 36);
  setupHeater(bedroomHeatData, bedroomHeater, onBedroomHeater, 37);
  setupHeater(bathroomHeatData, bathroomHeater, onBathroomHeater, 38);
  setupHeater(airHeatData, inletAirHeater, onInletAirHeater, 39);

  DomovoyDevice *fanDevs[2] = {&inletFan, &exhaustFan};
  for (uint8_t i = 0; i < 2; i++) {
    fans[i].startTimer = new TimerMs(4000, 0, 1);
    fans[i].speed = 0;
    fans[i].reported = 0;
    fans[i].startToSpeed = 0;
    fans[i].dev = fanDevs[i];
    fanDevs[i]->addNumber("fan_speed", "%", 0, 100, /*writable*/ true);
    hub.addDevice(*fanDevs[i]);
    fanDimmers.attach(i, 4 + i);
  }
  inletFan.onCommand(onInletFan);
  exhaustFan.onCommand(onExhaustFan);

  waterPressure.addBoolean("contact");
  hub.addDevice(waterPressure);

  co2Calibration.addBoolean("on_off", /*writable*/ true);
  co2Calibration.onCommand(onCalibrationSet);
  hub.addDevice(co2Calibration);
}

bool connectDomovoy() {
  lastConnectAttempt = millis();
  if (!hub.connect(HUB_ID)) return false;
  Serial.println(F("Domovoy connected"));
  publishAllStates();
  return true;
}

void setup() {
  setupNetwork();
  setupDimmerInterrupts();

  setupCO2();
  setupDHT();
  Serial.println(F("Sensors started..."));

  setupButtons();
  setupDevices();

  hub.setServer(DOMOVOY_SERVER, DOMOVOY_PORT);
  hub.setCredentials(MQTT_USER, MQTT_PASS);
  connectDomovoy();

  Serial.println(F("Start..."));
  Serial.println(Ethernet.localIP());
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

void toggleVirtualSwitch(uint8_t i) {
  switchStates[i] = switchStates[i] ? 0 : 1;
  publishBool(*virtualSwitches[i], "on_off", switchStates[i]);
}

// Кнопки потолочного света (A0..A2) и выключателей санузла/зеркала (A3, A4)
void handleLightButtons() {
  for (uint8_t i = 0; i < 5; i++) {
    dimBtn[i].tick();

    if (!dimBtn[i].click()) continue;

    if (i < LIGHT_COUNT) {
      lightStates[i] = lightStates[i] ? 0 : 1;
      digitalWrite(LIGHT_RELAY_BASE + i, lightStates[i]);

      Serial.print(F("Light "));
      Serial.print(i);
      Serial.print(F(" state "));
      Serial.println(lightStates[i]);

      publishBool(*lights[i], "on_off", lightStates[i]);
      continue;
    }

    toggleVirtualSwitch(i - 3); // A3 → санузел (0), A4 → зеркало (1)
  }
}

// Кнопка A5 — прожектор во дворе. Одиночный клик (click() вместо hasClicks(1):
// без двойного клика не нужно ждать таймаут различения — реакция мгновенная).
void handleYardButton() {
  dimBtn[5].tick();

  if (dimBtn[5].click()) toggleVirtualSwitch(2);
}

void handleWaterSensor() {
  if (waterSensorButton.tick()) {
    if (waterSensorButton.hold()) {
      waterState = true;
      publishBool(waterPressure, "contact", true);
    }
    if (waterSensorButton.release()) {
      waterState = false;
      publishBool(waterPressure, "contact", false);
    }
  }
}

// Перевод вентиляторов с разгонной скорости на заданную низкую после старта
void handleFanRampUp() {
  for (uint8_t i = 0; i < 2; i++) {
    if (fans[i].startTimer->tick() && fans[i].startToSpeed > 0) {
      fans[i].startTimer->stop();

      fans[i].speed = fans[i].startToSpeed;
      fanDimmers.write(i, map(fans[i].speed, 0, 100, 0, 250));
      fans[i].startToSpeed = 0;
    }
  }
}

// Программный ШИМ конвекторов и нагревателя воздуха.
// Мощности намеренно разнесены по фазам периода (зал/кухня включаются в начале,
// спальня/ванная — в конце), чтобы не нагружать сеть одновременно (краткое наслоение допустимо).
void handleHeaters() {
  // Рестарт таймера
  if (convTimer1.tick()) {
    digitalWrite(hallHeatData.pin, 1);
    hallHeatData.state = true;

    digitalWrite(kitchenHeatData.pin, 1);
    kitchenHeatData.state = true;

    digitalWrite(bedroomHeatData.pin, 0);
    bedroomHeatData.state = false;

    digitalWrite(bathroomHeatData.pin, 0);
    bathroomHeatData.state = false;
  }

  if (airHeaterTimer.tick()) {
    digitalWrite(airHeatData.pin, 0);
    airHeatData.state = false;
  }

  if (airHeaterTimer.active()) {
    if (airHeaterTimer.timeLeft8() < airHeatData.value && airHeaterTimer.timeLeft8() > 0) {
      if (!airHeatData.state) {
        digitalWrite(airHeatData.pin, 1);
        airHeatData.state = true;
      }
    }
  }

  // Включение конвекторов
  if (convTimer1.active()) {
    if (convTimer1.timeLeft8() < 255 - hallHeatData.value && convTimer1.timeLeft8() > 0) {
      if (hallHeatData.state) {
        digitalWrite(hallHeatData.pin, 0);
        hallHeatData.state = false;
      }
    }

    if (convTimer1.timeLeft8() < 255 - kitchenHeatData.value && convTimer1.timeLeft8() > 0) {
      if (kitchenHeatData.state) {
        digitalWrite(kitchenHeatData.pin, 0);
        kitchenHeatData.state = false;
      }
    }

    if (convTimer1.timeLeft8() < bedroomHeatData.value && convTimer1.timeLeft8() > 0) {
      if (!bedroomHeatData.state) {
        digitalWrite(bedroomHeatData.pin, 1);
        bedroomHeatData.state = true;
      }
    }

    if (convTimer1.timeLeft8() < bathroomHeatData.value && convTimer1.timeLeft8() > 0) {
      if (!bathroomHeatData.state) {
        digitalWrite(bathroomHeatData.pin, 1);
        bathroomHeatData.state = true;
      }
    }
  }
}

// Опрос DHT и CO2, публикация значений в Домовой
void readSensors() {
  if (!sensorGetTimer.tick()) return;

  float h1 = dht1.readHumidity();
  float t1 = dht1.readTemperature();
  if (!isnan(h1) && !isnan(t1)) {
    publishClimate(hallClimate, t1, h1);

    Serial.print(F("dht1: "));
    Serial.print(t1);
    Serial.print('_');
    Serial.println(h1);
  }

  float h2 = dht2.readHumidity();
  float t2 = dht2.readTemperature();
  if (!isnan(h2) && !isnan(t2)) {
    publishClimate(bedroomClimate, t2, h2);

    Serial.print(F("dht2: "));
    Serial.print(t2);
    Serial.print('_');
    Serial.println(h2);
  }

  int CO2_1 = co2_1.getCO2();
  if (co2_1.errorCode == RESULT_OK) {
    publishNumber(hallClimate, "co2", CO2_1);
    Serial.print(F("Co2_1: "));
    Serial.println(CO2_1);
  } else {
    Serial.print(F("Co2_1 errorCode: "));
    Serial.println(co2_1.errorCode);
    co2_1.recoveryReset();
  }

  int CO2_2 = co2_2.getCO2();
  if (co2_2.errorCode == RESULT_OK) {
    publishNumber(bedroomClimate, "co2", CO2_2);
    Serial.print(F("Co2_2: "));
    Serial.println(CO2_2);
  } else {
    Serial.print(F("Co2_2 errorCode: "));
    Serial.println(co2_2.errorCode);
    co2_2.recoveryReset();
  }
}

// Завершение калибровки CO2 по таймеру (запущена командой co2-calibration).
// Принудительная калибровка нуля: calibrate() приравнивает текущий воздух к 400 ppm,
// поэтому к этому моменту комната должна быть проветрена. ABC НЕ включаем — остаётся off.
void handleCO2Calibration() {
  if (co2Calibrating && calibrateTimer.ready()) {
    co2Calibrating = false;
    co2_1.calibrate(); // принудительная калибровка нуля (400 ppm)
    co2_2.calibrate();

    publishBool(co2Calibration, "on_off", false);
  }
}

void loop() {
  Ethernet.maintain();
  maintainDomovoy();

  handleLightButtons();
  handleYardButton();
  handleWaterSensor();

  handleFanRampUp();
  handleHeaters();

  readSensors();
  handleCO2Calibration();
}
