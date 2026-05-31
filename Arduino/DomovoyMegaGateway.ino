/*
 * Domovoy Arduino Mega Gateway — Domovoy Native protocol v1.
 *
 * ONE board fronts MULTIPLE logical devices over a single MQTT connection (like a Zigbee bridge).
 * Each device has its own deviceId, capabilities and topics domovoy/native/<deviceId>/...:
 *   - garage-relay     : actuator  (on_off)
 *   - greenhouse-climate: sensor   (temperature + humidity from one DHT)
 *   - hallway-motion    : sensor   (occupancy)
 *
 * This shows the corrected model: the board's MQTT client id is just the connection id — the
 * logical devices are independent. Production firmware would load these device definitions from
 * EEPROM/config; here they are declared statically for clarity.
 */

#include <DomovoyClient.h>
#include <Ethernet.h>
#include <SPI.h>

// ---- Network -----------------------------------------------------------------
byte mac[] = {0xDE, 0xAD, 0xBE, 0xEF, 0xFE, 0xED};
IPAddress ip(192, 168, 1, 177);
const char *mqtt_server = "192.168.1.100";
const char *mqtt_user = "domovoy";
const char *mqtt_pass = "password";

// ---- Pin map -----------------------------------------------------------------
const int RELAY_PIN = 13;
const int MOTION_PIN = 8;
// (greenhouse-climate is read from a DHT/analog sensor — simulated here)

EthernetClient ethClient;
DomovoyHub hub(ethClient);

// ---- Logical devices fronted by this one board -------------------------------
DomovoyDevice relay("garage-relay", "Garage Relay", "Mega Gateway", "2.0");
DomovoyDevice climate("greenhouse-climate", "Greenhouse Climate", "Mega Gateway", "2.0");
DomovoyDevice motion("hallway-motion", "Hallway Motion", "Mega Gateway", "2.0");

unsigned long lastSensorPublish = 0;
const unsigned long SENSOR_INTERVAL = 5000;

void publishRelay() {
  StaticJsonDocument<64> doc;
  doc["on_off"] = (digitalRead(RELAY_PIN) == HIGH);
  hub.publishState(relay, doc);
}

void publishClimate() {
  StaticJsonDocument<96> doc;
  doc["temperature"] = readTemperature(); // °C
  doc["humidity"] = readHumidity();        // %
  hub.publishState(climate, doc);
}

void publishMotion() {
  StaticJsonDocument<64> doc;
  doc["occupancy"] = (digitalRead(MOTION_PIN) == HIGH);
  hub.publishState(motion, doc);
}

void onRelayCommand(JsonObject set) {
  if (set.containsKey("on_off")) {
    digitalWrite(RELAY_PIN, set["on_off"].as<bool>() ? HIGH : LOW);
    publishRelay();
  }
}

void setup() {
  Serial.begin(9600);
  pinMode(RELAY_PIN, OUTPUT);
  pinMode(MOTION_PIN, INPUT);

  if (Ethernet.begin(mac) == 0) Ethernet.begin(mac, ip);
  delay(1500);

  // Declare each device's capabilities.
  relay.addBoolean("on_off", /*writable*/ true);
  relay.onCommand(onRelayCommand);

  climate.addNumber("temperature", "°C", -20, 50, /*writable*/ false);
  climate.addNumber("humidity", "%", 0, 100, /*writable*/ false);

  motion.addBoolean("occupancy", /*writable*/ false);

  hub.setServer(mqtt_server, 1883);
  hub.setCredentials(mqtt_user, mqtt_pass);
  hub.addDevice(relay);
  hub.addDevice(climate);
  hub.addDevice(motion);

  if (hub.connect("mega-gateway-board")) {
    Serial.println("Connected to Domovoy");
    publishRelay();
    publishClimate();
    publishMotion();
  } else {
    Serial.println("Connection failed");
  }
}

void loop() {
  hub.loop();

  if (millis() - lastSensorPublish > SENSOR_INTERVAL) {
    lastSensorPublish = millis();
    publishClimate();
    publishMotion();
  }
}

// ---- Stub sensor reads (replace with a real DHT/analog driver) ----------------
float readTemperature() { return 21.0 + (analogRead(A0) % 100) / 10.0; }
float readHumidity() { return 40.0 + (analogRead(A1) % 400) / 10.0; }
