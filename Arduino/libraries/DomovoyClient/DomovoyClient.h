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
// F(). The only transient RAM buffers are the two below; shrink them with -D flags if RAM is tight.

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
                const char *model = nullptr, const char *firmware = nullptr);

  void addCapability(const char *id, const char *kind, bool writable = false,
                     const char *unit = nullptr, float minValue = NAN, float maxValue = NAN);
  void addBoolean(const char *id, bool writable = false);
  void addNumber(const char *id, const char *unit = nullptr,
                 float minValue = NAN, float maxValue = NAN, bool writable = false);

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
  void streamAnnounce(Print &out, const char *hub) const;
};

/// <summary>Owns the MQTT connection for one board and routes to/from its devices.</summary>
class DomovoyHub {
public:
  DomovoyHub(Client &net);

  void setServer(const char *server, uint16_t port);
  void setCredentials(const char *user, const char *password);

  bool addDevice(DomovoyDevice &device);

  bool connect(const char *hubId); // hubId = MQTT client id of the board (not a logical device)
  void loop();

  void announceAll();
  void publishState(DomovoyDevice &device, JsonDocument &state);
  void setAvailable(DomovoyDevice &device, bool online);

private:
  PubSubClient _mqtt;
  const char *_user = nullptr;
  const char *_password = nullptr;
  const char *_hubId = "domovoy-hub";
  DomovoyDevice *_devices[DOMOVOY_MAX_DEVICES];
  uint8_t _deviceCount = 0;

  static DomovoyHub *_active;
  static void _bridge(char *topic, byte *payload, unsigned int length);
  void handleMessage(char *topic, byte *payload, unsigned int length);
  DomovoyDevice *findDevice(const char *deviceId);

  static String topicFor(const char *deviceId, const char *suffix);
};

#endif
