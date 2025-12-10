#include "DomovoyClient.h"

DomovoyClient::DomovoyClient(Client &client) : _mqtt(client) { _port = 1883; }

void DomovoyClient::setServer(const char *server, uint16_t port) {
  _server = server;
  _port = port;
  _mqtt.setServer(_server, _port);

  // Set internal callback to route to user callback
  // PubSubClient requires a static function or lambda for callback if using
  // member? Actually PubSubClient uses standard function pointer or
  // std::function in newer versions. Standard PubSubClient uses `void
  // (*callback)(char*, uint8_t*, unsigned int)` We can use std::bind or lambda
  // if C++11 enabled, but Arduino is limited. Workaround: We set "this" in a
  // static pointer? Or assume user uses `_mqtt.setCallback`? We want to hide
  // `_mqtt` details. Let's use `std::bind` style logic if possible OR use a
  // static instance pointer. For simplicity given standard Arduino C++, let's
  // use a simpler approach: We pass `_mqttCallback` which is a member... wait,
  // cannot pass member function as C callback. We will assume single instance
  // or use a static delegate. For now, simple implementation: We expose
  // `PubSubClient` for callback? No.

  // We will pass a static relay.
}

static DomovoyClient *_instance = nullptr;

void _staticMqttCallback(char *topic, byte *payload, unsigned int length) {
  if (_instance) {
    _instance->_mqttCallback(topic, payload, length);
  }
}

void DomovoyClient::setCredentials(const char *user, const char *password) {
  _user = user;
  _password = password;
}

bool DomovoyClient::connect(const char *clientId) {
  _instance = this;
  _mqtt.setCallback(_staticMqttCallback);

  if (_user && _password) {
    return _mqtt.connect(clientId, _user, _password);
  }
  return _mqtt.connect(clientId);
}

void DomovoyClient::loop() { _mqtt.loop(); }

void DomovoyClient::setCallback(CommandCallback callback) {
  _callback = callback;
}

void DomovoyClient::_mqttCallback(char *topic, byte *payload,
                                  unsigned int length) {
  // Parse command
  // Expected JSON: { "action": "TurnOn", "params": { ... } }

  StaticJsonDocument<512> doc;
  DeserializationError error = deserializeJson(doc, payload, length);

  if (error) {
    return; // Silent fail
  }

  const char *action = doc["action"];
  JsonObject params = doc["params"];

  if (_callback && action) {
    _callback(String(action), params);
  }
}

void DomovoyClient::announce(DeviceType type, const char *friendlyName,
                             const char *uniqueId, JsonObject metadata) {
  // Topic: domovoy/discovery/{type}/{name}/announce
  String typeStr;
  switch (type) {
  case light:
    typeStr = "light";
    break;
  case sensor:
    typeStr = "sensor";
    break;
  case switch_device:
    typeStr = "switch";
    break; // mapped to "Switch" in backend
  default:
    typeStr = "generic";
    break;
  }

  String topic =
      "domovoy/discovery/" + typeStr + "/" + String(friendlyName) + "/announce";
  String cmdTopic = "domovoy/device/" + String(friendlyName) + "/set";
  String stateTopic = "domovoy/device/" + String(friendlyName);

  StaticJsonDocument<512> doc;
  doc["deviceId"] = uniqueId;
  doc["name"] = friendlyName; // This maps to DeviceDiscoveredEvent.Name
  doc["deviceType"] = typeStr;
  doc["source"] = "Arduino";

  JsonObject meta = doc.createNestedObject("metadata");
  meta["command_topic"] = cmdTopic;
  meta["state_topic"] = stateTopic;

  // Merge user metadata
  for (JsonPair p : metadata) {
    meta[p.key()] = p.value();
  }

  char buffer[512];
  serializeJson(doc, buffer);

  _mqtt.publish(topic.c_str(), buffer, true); // Retain announcement

  // Subscribe to command topic
  _mqtt.subscribe(cmdTopic.c_str());
}

void DomovoyClient::publishState(const char *friendlyName,
                                 const char *payload) {
  String topic = "domovoy/device/" + String(friendlyName);
  _mqtt.publish(topic.c_str(), payload);
}

void DomovoyClient::publishState(const char *friendlyName, JsonDocument &doc) {
  String topic = "domovoy/device/" + String(friendlyName);
  char buffer[512];
  serializeJson(doc, buffer);
  _mqtt.publish(topic.c_str(), buffer);
}
