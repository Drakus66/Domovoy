#ifndef DomovoyClient_h
#define DomovoyClient_h

#include "Arduino.h"
#include <ArduinoJson.h>
#include <PubSubClient.h>


enum DeviceType {
  generic,
  light,
  sensor,
  switch_device // 'switch' is a keyword
};

typedef void (*CommandCallback)(String deviceName, String action,
                                JsonObject params);

class DomovoyClient {
public:
  DomovoyClient(Client &client);
  void setServer(const char *server, uint16_t port);
  void setCallback(CommandCallback callback);
  void setCredentials(const char *user, const char *password);

  bool connect(const char *clientId);
  void loop();

  // Discovery
  void announce(DeviceType type, const char *friendlyName, const char *uniqueId,
                JsonObject metadata = JsonObject());

  // State publishing
  void publishState(const char *friendlyName, const char *payload);
  void publishState(const char *friendlyName, JsonDocument &doc);

private:
  PubSubClient _mqtt;
  const char *_server;
  uint16_t _port;
  const char *_user;
  const char *_password;
  CommandCallback _callback;

  void _mqttCallback(char *topic, byte *payload, unsigned int length);
};

#endif
