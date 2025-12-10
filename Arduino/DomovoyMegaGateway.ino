/*
 * Domovoy Arduino Mega Gateway (Refactored)
 * Uses DomovoyClient library for MQTT communication
 */

#include <ArduinoJson.h>
#include <DomovoyClient.h>
#include <EEPROM.h>
#include <Ethernet.h>
#include <PubSubClient.h>
#include <SPI.h>


// Configuration Defaults
#define CONFIG_EEPROM_ADDR 0
#define CONFIG_VERSION                                                         \
  2 // Bump version for new structure if needed, or keep 1 compatible? Start
    // fresh.

struct GatewayConfig {
  uint8_t configVersion;
  char mqttServer[40];
  uint16_t mqttPort;
  char mqttUsername[20];
  char mqttPassword[20];
  char mqttClientId[30];
  bool dhcpEnabled;
  byte ip[4];
  byte gateway[4];
  byte subnet[4];
  byte dns[4];
  byte mac[6];
  // Pin Configuration
  uint8_t pinModes[70]; // 0=Unused, 1=Input, 2=Output, 3=InputPullup, 4=Analog,
                        // 5=PWM
  char pinLabels[70][20];
};

GatewayConfig config;

// Ethernet & Client
EthernetClient ethClient;
DomovoyClient client(ethClient);

// Timing
unsigned long lastSensorUpdate = 0;
const unsigned long SENSOR_READ_INTERVAL = 5000;

// Defaults
byte defaultMac[] = {0xDE, 0xAD, 0xBE, 0xEF, 0xFE, 0xED};
IPAddress defaultIp(192, 168, 1, 177);

void loadConfiguration() {
  if (EEPROM.read(CONFIG_EEPROM_ADDR) == CONFIG_VERSION) {
    EEPROM.get(CONFIG_EEPROM_ADDR, config);
  } else {
    // Defaults
    config.configVersion = CONFIG_VERSION;
    strcpy(config.mqttServer, "192.168.1.100");
    config.mqttPort = 1883;
    strcpy(config.mqttUsername, "domovoy");
    strcpy(config.mqttPassword, "password");
    strcpy(config.mqttClientId, "mega-gateway");
    config.dhcpEnabled = true;
    memcpy(config.mac, defaultMac, 6);
    memcpy(config.ip, defaultIp, 4);

    // Clear pins
    for (int i = 0; i < 70; i++) {
      config.pinModes[i] = 0;
      strcpy(config.pinLabels[i], "");
    }

    // Example default pin
    config.pinModes[13] = 2; // Output
    strcpy(config.pinLabels[13], "StatusLED");

    EEPROM.put(CONFIG_EEPROM_ADDR, config);
  }
}

void setupPins() {
  for (int i = 0; i < 70; i++) {
    if (config.pinModes[i] > 0) {
      switch (config.pinModes[i]) {
      case 1:
        pinMode(i, INPUT);
        break;
      case 2:
        pinMode(i, OUTPUT);
        break;
      case 3:
        pinMode(i, INPUT_PULLUP);
        break;
      case 5:
        pinMode(i, OUTPUT);
        break;
        // 4 is analog, no pinMode needed
      }
    }
  }
}

// Find pin index by friendly name
int findPinByName(String name) {
  for (int i = 0; i < 70; i++) {
    if (config.pinModes[i] > 0 && String(config.pinLabels[i]) == name) {
      return i;
    }
  }
  return -1;
}

void callback(String deviceName, String action, JsonObject params) {
  int pin = findPinByName(deviceName);
  if (pin == -1)
    return;

  if (action == "SetState") {
    // Check mode
    int mode = config.pinModes[pin];
    if (mode == 2 || mode == 5) { // Output
      if (params.containsKey("state")) {
        const char *st = params["state"];
        if (strcmp(st, "ON") == 0) {
          digitalWrite(pin, HIGH);
          client.publishState(deviceName.c_str(), "{\"state\": \"ON\"}");
        } else if (strcmp(st, "OFF") == 0) {
          digitalWrite(pin, LOW);
          client.publishState(deviceName.c_str(), "{\"state\": \"OFF\"}");
        }
      }
      if (params.containsKey("brightness") && mode == 5) {
        int val = params["brightness"];
        analogWrite(pin, val);
        // publish state...
      }
    }
  }
}

void announceDevices() {
  StaticJsonDocument<256> meta;
  meta["gateway"] = config.mqttClientId;

  for (int i = 0; i < 70; i++) {
    if (config.pinModes[i] > 0) {
      String name = String(config.pinLabels[i]);
      if (name.length() == 0)
        name = "Pin_" + String(i);

      String uid = String(config.mqttClientId) + "_" + String(i);
      meta["pin"] = i;

      DeviceType type = generic;
      if (config.pinModes[i] == 2 || config.pinModes[i] == 5)
        type = switch_device; // or Light?
      if (config.pinModes[i] == 1 || config.pinModes[i] == 3 ||
          config.pinModes[i] == 4)
        type = sensor;

      client.announce(type, name.c_str(), uid.c_str(), meta.as<JsonObject>());
    }
  }
}

void setup() {
  Serial.begin(9600);
  loadConfiguration();
  setupPins();

  // Ethernet
  if (config.dhcpEnabled) {
    if (Ethernet.begin(config.mac) == 0) {
      Ethernet.begin(config.mac, config.ip); // Fallback
    }
  } else {
    Ethernet.begin(config.mac, config.ip);
  }
  delay(1500);

  client.setServer(config.mqttServer, config.mqttPort);
  client.setCredentials(config.mqttUsername, config.mqttPassword);
  client.setCallback(callback);

  if (client.connect(config.mqttClientId)) {
    Serial.println("Connected to MQTT");
    announceDevices();
  }
}

void loop() {
  client.loop();

  if (millis() - lastSensorUpdate > SENSOR_READ_INTERVAL) {
    lastSensorUpdate = millis();
    // Read sensors
    for (int i = 0; i < 70; i++) {
      int mode = config.pinModes[i];
      if (mode == 1 || mode == 3) { // Digital Input
        int val = digitalRead(i);
        String name = String(config.pinLabels[i]);
        if (name.length() == 0)
          name = "Pin_" + String(i);
        // optimization: report only change?
        StaticJsonDocument<128> doc;
        doc["value"] = val;
        client.publishState(name.c_str(), doc);
      } else if (mode == 4) { // Analog
        int val = analogRead(i);
        String name = String(config.pinLabels[i]);
        if (name.length() == 0)
          name = "Pin_" + String(i);
        StaticJsonDocument<128> doc;
        doc["value"] = val;
        client.publishState(name.c_str(), doc);
      }
    }
  }
}