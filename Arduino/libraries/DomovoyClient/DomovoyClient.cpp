#include "DomovoyClient.h"

namespace {
// A Print sink that only counts the bytes written. Streaming the announce through it gives the
// exact MQTT payload length for beginPublish() without ever holding the JSON in RAM.
class CountingPrint : public Print {
public:
  size_t count = 0;
  size_t write(uint8_t) override { count++; return 1; }
  size_t write(const uint8_t *, size_t n) override { count += n; return n; }
};
} // namespace

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

// Hand-written so no JsonDocument is buffered. All literals stay in flash (F()); only the
// user-supplied ids/units (already in RAM) are streamed as-is. ids/units are simple tokens with
// no quotes/backslashes, so no JSON escaping is needed.
void DomovoyDevice::streamAnnounce(Print &out, const char *hub) const {
  out.print(F("{\"deviceId\":\""));
  out.print(_id);
  out.print(F("\",\"name\":\""));
  out.print(_name);
  out.print(F("\",\"hub\":\""));
  out.print(hub);
  out.print('"');
  if (_model) { out.print(F(",\"model\":\"")); out.print(_model); out.print('"'); }
  if (_firmware) { out.print(F(",\"firmware\":\"")); out.print(_firmware); out.print('"'); }

  out.print(F(",\"capabilities\":["));
  for (uint8_t i = 0; i < _capCount; i++) {
    if (i) out.print(',');
    const Cap &c = _caps[i];
    out.print(F("{\"id\":\""));
    out.print(c.id);
    out.print(F("\",\"kind\":\""));
    out.print(c.kind);
    out.print(F("\",\"attributes\":{\"writable\":"));
    out.print(c.writable ? F("true") : F("false"));
    if (c.unit) { out.print(F(",\"unit\":\"")); out.print(c.unit); out.print('"'); }
    if (!isnan(c.minV)) { out.print(F(",\"min\":")); out.print(c.minV); }
    if (!isnan(c.maxV)) { out.print(F(",\"max\":")); out.print(c.maxV); }
    out.print(F("}}"));
  }
  out.print(F("]}"));
}

// ===================== DomovoyHub =====================

DomovoyHub *DomovoyHub::_active = nullptr;

DomovoyHub::DomovoyHub(Client &net) : _mqtt(net) {}

void DomovoyHub::setServer(const char *server, uint16_t port) {
  _mqtt.setServer(server, port);
  // Bounds inbound /set payloads and non-streamed state publishes. Announcements are streamed, so
  // they are NOT limited by this. Keep small on RAM-constrained boards (see DOMOVOY_MQTT_BUFFER).
  _mqtt.setBufferSize(DOMOVOY_MQTT_BUFFER);
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
    _mqtt.subscribe("domovoy/native/+/set");   // one subscription routes all devices' commands
    _mqtt.subscribe("domovoy/native/discover"); // server broadcast → re-announce all devices
    announceAll();
  }
  return ok;
}

void DomovoyHub::loop() { _mqtt.loop(); }

void DomovoyHub::announceAll() {
  for (uint8_t i = 0; i < _deviceCount; i++) {
    DomovoyDevice *d = _devices[i];

    // First pass measures the exact length; second pass streams the bytes. No JSON is buffered.
    CountingPrint counter;
    d->streamAnnounce(counter, _hubId);

    String topic = topicFor(d->id(), "announce");
    if (_mqtt.beginPublish(topic.c_str(), counter.count, true)) {
      d->streamAnnounce(_mqtt, _hubId); // PubSubClient is a Print
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
  // Server broadcast asking every device to re-announce. The server sends this on (re)start because
  // RabbitMQ does not redeliver retained announces to its wildcard subscription, so without it our
  // devices would be in the read-model yet unroutable for commands.
  if (strcmp(topic, "domovoy/native/discover") == 0) {
    announceAll();
    return;
  }

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

  StaticJsonDocument<DOMOVOY_CMD_DOC_SIZE> doc;
  if (deserializeJson(doc, payload, length)) return; // ignore malformed
  JsonObject set = doc.as<JsonObject>();
  if (!set.isNull()) device->_cb(set);
}
