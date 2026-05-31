/*
  SimpleLed.ino — Example for DomovoyClient (Domovoy Native protocol v1).
  A board fronting a SINGLE logical device: an LED exposed as the writable boolean "on_off".
  (A board can front many devices — see DomovoyMegaGateway.ino.)
*/

#include <DomovoyClient.h>
#include <Ethernet.h>
#include <SPI.h>

byte mac[] = {0xDE, 0xED, 0xBA, 0xFE, 0xFE, 0xED};
IPAddress ip(192, 168, 1, 150);
const char *mqtt_server = "192.168.1.100"; // Domovoy server (RabbitMQ MQTT) IP

EthernetClient ethClient;
DomovoyHub hub(ethClient);
DomovoyDevice led("domovoy-led-01", "Simple LED", "Domovoy DIY LED", "1.0");

const int ledPin = 13;
bool ledOn = false;

void publishLed() {
  StaticJsonDocument<64> doc;
  doc["on_off"] = ledOn;
  hub.publishState(led, doc);
}

void onLedCommand(JsonObject set) {
  if (set.containsKey("on_off")) {
    ledOn = set["on_off"].as<bool>();
    digitalWrite(ledPin, ledOn ? HIGH : LOW);
    publishLed();
  }
}

void setup() {
  pinMode(ledPin, OUTPUT);
  Serial.begin(9600);

  if (Ethernet.begin(mac) == 0) Ethernet.begin(mac, ip);
  delay(1500);

  led.addBoolean("on_off", /*writable*/ true);
  led.onCommand(onLedCommand);

  hub.setServer(mqtt_server, 1883);
  hub.addDevice(led);

  if (hub.connect("domovoy-led-board")) {
    Serial.println("Connected to Domovoy");
    publishLed();
  } else {
    Serial.println("Connection failed");
  }
}

void loop() {
  hub.loop();
}
