#include "DomovoyClient.h"

// ===================== DomovoyDevice =====================

DomovoyDevice::DomovoyDevice(const char *deviceId, const char *name,
                             const char *model, const char *firmware)
    : _id(deviceId), _name(name), _model(model), _firmware(firmware) {}

void DomovoyDevice::addCapability(const char *id, const char *kind, bool writable,
                                  const char *unit, float minValue, float maxValue) {
  if (_capCount >= DOMOVOY_MAX_CAPS) return;
  _caps[_capCount++] = Cap{id, kind, writable, unit, minValue, maxValue};
}

void DomovoyDevice::addBoolean(const char *id, bool writable) {
  addCapability(id, "Boolean", writable);
}

void DomovoyDevice::addNumber(const char *id, const char *unit,
                              float minValue, float maxValue, bool writable) {
  addCapability(id, "Number", writable, unit, minValue, maxValue);
}

void DomovoyDevice::writeAnnounce(JsonDocument &doc) const {
  doc["deviceId"] = _id;
  doc["name"] = _name;
  if (_model) doc["model"] = _model;
  if (_firmware) doc["firmware"] = _firmware;

  JsonArray caps = doc.createNestedArray("capabilities");
  for (uint8_t i = 0; i < _capCount; i++) {
    JsonObject c = caps.createNestedObject();
    c["id"] = _caps[i].id;
    c["kind"] = _caps[i].kind;
    JsonObject attrs = c.createNestedObject("attributes");
    attrs["writable"] = _caps[i].writable;
    if (_caps[i].unit) attrs["unit"] = _caps[i].unit;
    if (!isnan(_caps[i].minV)) attrs["min"] = _caps[i].minV;
    if (!isnan(_caps[i].maxV)) attrs["max"] = _caps[i].maxV;
  }
}

// ===================== DomovoyHub =====================

DomovoyHub *DomovoyHub::_active = nullptr;

DomovoyHub::DomovoyHub(Client &net) : _mqtt(net) {}

void DomovoyHub::setServer(const char *server, uint16_t port) {
  _mqtt.setServer(server, port);
  // Headroom for the MQTT header + inbound command payloads. Announcements are streamed.
  _mqtt.setBufferSize(512);
}

void DomovoyHub::setCredentials(const char *user, const char *password) {
  _user = user;
  _password = password;
}

bool DomovoyHub::addDevice(DomovoyDevice &device) {
  if (_deviceCount >= DOMOVOY_MAX_DEVICES) return false;
  _devices[_deviceCount++] = &device;
  return true;
}

bool DomovoyHub::connect(const char *hubId) {
  _hubId = hubId;
  _active = this;
  _mqtt.setCallback(_bridge);

  // Board reachability (separate namespace, not a logical device) — LWT marks the board offline.
  String status = String("domovoy/hub/") + hubId + "/status";

  bool ok = (_user && _password)
                ? _mqtt.connect(hubId, _user, _password, status.c_str(), 1, true, "offline")
                : _mqtt.connect(hubId, nullptr, nullptr, status.c_str(), 1, true, "offline");

  if (ok) {
    _mqtt.publish(status.c_str(), "online", true);
    _mqtt.subscribe("domovoy/native/+/set"); // one subscription routes all devices' commands
    announceAll();
  }
  return ok;
}

void DomovoyHub::loop() { _mqtt.loop(); }

void DomovoyHub::announceAll() {
  for (uint8_t i = 0; i < _deviceCount; i++) {
    DomovoyDevice *d = _devices[i];

    StaticJsonDocument<DOMOVOY_ANNOUNCE_DOC_SIZE> doc;
    d->writeAnnounce(doc);

    String topic = topicFor(d->id(), "announce");
    if (_mqtt.beginPublish(topic.c_str(), measureJson(doc), true)) {
      serializeJson(doc, _mqtt); // PubSubClient is a Print
      _mqtt.endPublish();
    }
    setAvailable(*d, true);
  }
}

void DomovoyHub::publishState(DomovoyDevice &device, JsonDocument &state) {
  String topic = topicFor(device.id(), "state");
  if (_mqtt.beginPublish(topic.c_str(), measureJson(state), false)) {
    serializeJson(state, _mqtt);
    _mqtt.endPublish();
  }
}

void DomovoyHub::setAvailable(DomovoyDevice &device, bool online) {
  String topic = topicFor(device.id(), "availability");
  _mqtt.publish(topic.c_str(), online ? "online" : "offline", true); // retained
}

String DomovoyHub::topicFor(const char *deviceId, const char *suffix) {
  return String("domovoy/native/") + deviceId + "/" + suffix;
}

DomovoyDevice *DomovoyHub::findDevice(const char *deviceId) {
  for (uint8_t i = 0; i < _deviceCount; i++)
    if (strcmp(_devices[i]->id(), deviceId) == 0) return _devices[i];
  return nullptr;
}

void DomovoyHub::_bridge(char *topic, byte *payload, unsigned int length) {
  if (_active) _active->handleMessage(topic, payload, length);
}

void DomovoyHub::handleMessage(char *topic, byte *payload, unsigned int length) {
  // Expect: domovoy/native/<deviceId>/set
  const char *prefix = "domovoy/native/";
  size_t plen = strlen(prefix);
  if (strncmp(topic, prefix, plen) != 0) return;

  const char *rest = topic + plen; // "<deviceId>/set"
  const char *slash = strchr(rest, '/');
  if (!slash || strcmp(slash + 1, "set") != 0) return;

  char deviceId[32];
  size_t idLen = (size_t)(slash - rest);
  if (idLen == 0 || idLen >= sizeof(deviceId)) return;
  memcpy(deviceId, rest, idLen);
  deviceId[idLen] = '\0';

  DomovoyDevice *device = findDevice(deviceId);
  if (!device || !device->_cb) return;

  StaticJsonDocument<512> doc;
  if (deserializeJson(doc, payload, length)) return; // ignore malformed
  JsonObject set = doc.as<JsonObject>();
  if (!set.isNull()) device->_cb(set);
}
