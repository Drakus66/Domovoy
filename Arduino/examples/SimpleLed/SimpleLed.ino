/*
  SimpleLed.ino - Example for DomovoyClient
  Controls logic of an LED via Domovoy Native Protocol
*/

#include <DomovoyClient.h>
#include <Ethernet.h>
#include <SPI.h>

// Update these with values suitable for your network.
byte mac[] = {0xDE, 0xED, 0xBA, 0xFE, 0xFE, 0xED};
IPAddress ip(192, 168, 1, 150);
const char *mqtt_server = "192.168.1.100"; // Domovoy server IP

EthernetClient ethClient;
DomovoyClient client(ethClient);

const int ledPin = 13;

void callback(String deviceName, String action, JsonObject params) {
  Serial.print("Device: ");
  Serial.println(deviceName);
  Serial.print("Action: ");
  Serial.println(action);

  if (action == "SetState") { // DeviceCommandTypes.SetState
    // Extract parameters
    if (params.containsKey("state")) {
      const char *state = params["state"];
      if (strcmp(state, "ON") == 0) {
        digitalWrite(ledPin, HIGH);
        client.publishState("SimpleLed", "{\"state\": \"ON\"}");
      } else if (strcmp(state, "OFF") == 0) {
        digitalWrite(ledPin, LOW);
        client.publishState("SimpleLed", "{\"state\": \"OFF\"}");
      }
    }
  }
}

void setup() {
  pinMode(ledPin, OUTPUT);
  Serial.begin(9600);

  // Start Ethernet
  if (Ethernet.begin(mac) == 0) {
    Serial.println("Failed to configure Ethernet using DHCP");
    Ethernet.begin(mac, ip);
  }
  delay(1500); // Allow hardware to initialize

  client.setServer(mqtt_server, 1883);
  client.setCallback(callback);

  if (client.connect("DomovoyClient-LED")) {
    Serial.println("Connected to MQTT");

    // Announce device
    // Type: light, Name: SimpleLed, ID: unique-id-123
    StaticJsonDocument<200> meta;
    meta["description"] = "Example LED";

    client.announce(light, "SimpleLed", "unique-id-123", meta.as<JsonObject>());

  } else {
    Serial.println("Connection failed");
  }
}

void loop() { client.loop(); }
