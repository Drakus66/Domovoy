#ifndef DomovoyClient_h
#define DomovoyClient_h

#include "Arduino.h"
#include <ArduinoJson.h>
#include <PubSubClient.h>

// One physical board / shield fronts MANY logical devices over a single MQTT connection
// (like a Zigbee2MQTT bridge). Each DomovoyDevice has its own deviceId, capabilities and topics:
//   domovoy/native/<deviceId>/{announce,state,set,availability}
// The board's MQTT client id ("hubId") is just the connection identifier — NOT a logical device.
// Board reachability lives at domovoy/hub/<hubId>/status, where the single MQTT Last-Will fires
// "offline" on an ungraceful drop; the server then marks every device of that hub offline (MQTT
// allows only one will per connection, so it can't be attached per-device).
//
// MEMORY (AVR / Arduino Nano has ~2 KB SRAM): announcements are streamed straight to the MQTT
// socket — no JSON document is ever buffered — and every fixed JSON fragment is kept in flash via
// F(). The only transient RAM buffers are the two below.
//
// The library is HEADER-ONLY on purpose: the sizing macros change the class layout, so they must
// be visible in the one translation unit that instantiates the classes. Override them with
// #define BEFORE #include <DomovoyClient.h> in the sketch (a separate .cpp would silently keep the
// defaults and corrupt memory — see the One Definition Rule).

#ifndef DOMOVOY_MAX_CAPS
#define DOMOVOY_MAX_CAPS 8       // max capabilities per device
#endif
#ifndef DOMOVOY_MAX_DEVICES
#define DOMOVOY_MAX_DEVICES 8    // max devices per board
#endif
#ifndef DOMOVOY_CMD_DOC_SIZE
#define DOMOVOY_CMD_DOC_SIZE 192 // transient parse buffer for one inbound /set payload
#endif
#ifndef DOMOVOY_MQTT_BUFFER
#define DOMOVOY_MQTT_BUFFER 256  // PubSubClient packet buffer (bounds inbound /set + state publishes)
#endif

// Receives the capability set { "<capabilityId>": <value>, ... } the server sent to this device.
typedef void (*DeviceCommandCallback)(JsonObject set);

/// <summary>One logical device: a stable id, a name and a set of capabilities.</summary>
class DomovoyDevice {
public:
  DomovoyDevice(const char *deviceId, const char *name,
                const char *model = nullptr, const char *firmware = nullptr)
      : _id(deviceId), _name(name), _model(model), _firmware(firmware) {}

  void addCapability(const char *id, const char *kind, bool writable = false,
                     const char *unit = nullptr, float minValue = NAN, float maxValue = NAN) {
    if (_capCount >= DOMOVOY_MAX_CAPS) return;
    _caps[_capCount++] = Cap{id, kind, writable, unit, minValue, maxValue};
  }

  void addBoolean(const char *id, bool writable = false) {
    addCapability(id, "Boolean", writable);
  }

  void addNumber(const char *id, const char *unit = nullptr,
                 float minValue = NAN, float maxValue = NAN, bool writable = false) {
    addCapability(id, "Number", writable, unit, minValue, maxValue);
  }

  void onCommand(DeviceCommandCallback cb) { _cb = cb; }
  const char *id() const { return _id; }

private:
  friend class DomovoyHub;

  struct Cap {
    const char *id;
    const char *kind;
    bool writable;
    const char *unit;
    float minV;
    float maxV;
  };

  const char *_id;
  const char *_name;
  const char *_model;
  const char *_firmware;
  Cap _caps[DOMOVOY_MAX_CAPS];
  uint8_t _capCount = 0;
  DeviceCommandCallback _cb = nullptr;

  // Streams the announce JSON directly to `out` (nothing is buffered). `hub` is the board's
  // connection id so the server can map device→hub for offline-on-crash handling.
  // Hand-written so no JsonDocument is buffered. All literals stay in flash (F()); only the
  // user-supplied ids/units (already in RAM) are streamed as-is. ids/units are simple tokens with
  // no quotes/backslashes, so no JSON escaping is needed.
  void streamAnnounce(Print &out, const char *hub) const {
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
};

/// <summary>Owns the MQTT connection for one board and routes to/from its devices.</summary>
class DomovoyHub {
public:
  DomovoyHub(Client &net) : _mqtt(net) {}

  void setServer(const char *server, uint16_t port) {
    _mqtt.setServer(server, port);
    // Bounds inbound /set payloads and non-streamed state publishes. Announcements are streamed, so
    // they are NOT limited by this. Keep small on RAM-constrained boards (see DOMOVOY_MQTT_BUFFER).
    _mqtt.setBufferSize(DOMOVOY_MQTT_BUFFER);
  }

  void setCredentials(const char *user, const char *password) {
    _user = user;
    _password = password;
  }

  bool addDevice(DomovoyDevice &device) {
    if (_deviceCount >= DOMOVOY_MAX_DEVICES) return false;
    _devices[_deviceCount++] = &device;
    return true;
  }

  // hubId = MQTT client id of the board (not a logical device)
  bool connect(const char *hubId) {
    _hubId = hubId;
    active() = this;
    _mqtt.setCallback(_bridge);

    // Board reachability (separate namespace, not a logical device) — LWT marks the board offline.
    String status = String("domovoy/hub/") + hubId + "/status";

    bool ok = (_user && _password)
                  ? _mqtt.connect(hubId, _user, _password, status.c_str(), 1, true, "offline")
                  : _mqtt.connect(hubId, nullptr, nullptr, status.c_str(), 1, true, "offline");

    if (ok) {
      _mqtt.publish(status.c_str(), "online", true);
      _mqtt.subscribe("domovoy/native/+/set");    // one subscription routes all devices' commands
      _mqtt.subscribe("domovoy/native/discover"); // server broadcast → re-announce all devices
      announceAll();
    }
    return ok;
  }

  bool connected() { return _mqtt.connected(); } // for firmware-side reconnect loops
  void loop() { _mqtt.loop(); }

  void announceAll() {
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

  void publishState(DomovoyDevice &device, JsonDocument &state) {
    String topic = topicFor(device.id(), "state");
    if (_mqtt.beginPublish(topic.c_str(), measureJson(state), false)) {
      serializeJson(state, _mqtt);
      _mqtt.endPublish();
    }
  }

  void setAvailable(DomovoyDevice &device, bool online) {
    String topic = topicFor(device.id(), "availability");
    _mqtt.publish(topic.c_str(), online ? "online" : "offline", true); // retained
  }

private:
  // A Print sink that only counts the bytes written. Streaming the announce through it gives the
  // exact MQTT payload length for beginPublish() without ever holding the JSON in RAM.
  class CountingPrint : public Print {
  public:
    size_t count = 0;
    size_t write(uint8_t) override { count++; return 1; }
    size_t write(const uint8_t *, size_t n) override { count += n; return n; }
  };

  PubSubClient _mqtt;
  const char *_user = nullptr;
  const char *_password = nullptr;
  const char *_hubId = "domovoy-hub";
  DomovoyDevice *_devices[DOMOVOY_MAX_DEVICES];
  uint8_t _deviceCount = 0;

  // Function-local static instead of a static data member: header-only + pre-C++17 (no inline
  // variables on AVR gcc), and PubSubClient's C callback needs to find the hub instance.
  static DomovoyHub *&active() {
    static DomovoyHub *hub = nullptr;
    return hub;
  }

  static void _bridge(char *topic, byte *payload, unsigned int length) {
    if (active()) active()->handleMessage(topic, payload, length);
  }

  void handleMessage(char *topic, byte *payload, unsigned int length) {
    // Server broadcast asking every device to re-announce. The server sends this on (re)start
    // because RabbitMQ does not redeliver retained announces to its wildcard subscription, so
    // without it our devices would be in the read-model yet unroutable for commands.
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

  DomovoyDevice *findDevice(const char *deviceId) {
    for (uint8_t i = 0; i < _deviceCount; i++)
      if (strcmp(_devices[i]->id(), deviceId) == 0) return _devices[i];
    return nullptr;
  }

  static String topicFor(const char *deviceId, const char *suffix) {
    return String("domovoy/native/") + deviceId + "/" + suffix;
  }
};

#endif
