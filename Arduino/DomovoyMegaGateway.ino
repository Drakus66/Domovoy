/*
 * Domovoy Arduino Mega Gateway
 * Базовый MQTT шлюз для системы умного дома Domovoy
 * 
 * Функциональность:
 * - Подключение к MQTT брокеру по Ethernet
 * - Автоматическое переподключение
 * - Обнаружение и объявление устройств
 * - Базовая работа с сенсорами
 * - Конфигурирование через MQTT команды
 */

 #include <SPI.h>
 #include <Ethernet.h>
 #include <PubSubClient.h>
 #include <ArduinoJson.h>
 #include <EEPROM.h>
 
 // Настройки Ethernet
 byte mac[] = { 0xDE, 0xAD, 0xBE, 0xEF, 0xFE, 0xED };  // MAC адрес (можно изменить)
 IPAddress ip(192, 168, 1, 177);                        // Статический IP (опционально)
 IPAddress gateway(192, 168, 1, 1);                    // Шлюз (опционально)
 IPAddress subnet(255, 255, 255, 0);                   // Маска подсети (опционально)
 IPAddress dns(192, 168, 1, 1);                        // DNS сервер (опционально)
 
 // Настройки MQTT
 #define MQTT_SERVER "192.168.1.100"                   // Адрес MQTT брокера
 #define MQTT_PORT 1883                                // Порт MQTT брокера
 #define MQTT_USERNAME "domovoy"                       // Логин для MQTT брокера
 #define MQTT_PASSWORD "password"                      // Пароль для MQTT брокера
 #define MQTT_CLIENT_ID "arduino-mega-gateway"         // ID клиента (должен быть уникальным)
 
 // MQTT топики
 #define MQTT_DISCOVERY_PREFIX "domovoy/discovery"     // Префикс для обнаружения устройств
 #define MQTT_COMMAND_TOPIC "domovoy/gateway/cmd"      // Топик для команд шлюзу
 #define MQTT_STATUS_TOPIC "domovoy/gateway/status"    // Топик для статуса шлюза
 #define MQTT_CONFIG_TOPIC "domovoy/gateway/config"    // Топик для конфигурации шлюза
 
 // Настройки для хранения конфигурации
 #define CONFIG_EEPROM_ADDR 0                          // Адрес в EEPROM для хранения конфигурации
 #define CONFIG_VERSION 1                              // Версия конфигурации
 
 // Статус LED индикаторы
 #define LED_STATUS 13                                 // Пин для индикации статуса
 #define LED_MQTT 12                                   // Пин для индикации MQTT соединения
 
 // Структура для хранения конфигурации
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
   // Конфигурация пинов
   uint8_t pinModes[70];  // Режимы пинов (Arduino Mega имеет 70 пинов)
   char pinLabels[70][20]; // Названия пинов
 };
 
 GatewayConfig config;
 bool configChanged = false;
 
 // Интервалы обновления и проверки соединения
 unsigned long lastReconnectAttempt = 0;     // Время последней попытки переподключения
 unsigned long lastStatusUpdate = 0;         // Время последней публикации статуса
 unsigned long lastSensorUpdate = 0;         // Время последнего считывания сенсоров
 const unsigned long RECONNECT_INTERVAL = 5000;      // Интервал попыток переподключения (мс)
 const unsigned long STATUS_INTERVAL = 60000;        // Интервал отправки статуса (мс)
 const unsigned long SENSOR_READ_INTERVAL = 10000;   // Интервал чтения сенсоров (мс)
 
 // Переменные для Ethernet и MQTT
 EthernetClient ethClient;
 PubSubClient mqttClient(ethClient);
 
 // Флаги состояний
 bool mqttConnected = false;
 bool networkConnected = false;
 
 // Функция для загрузки конфигурации из EEPROM
 void loadConfiguration() {
   // Если в EEPROM хранится правильная версия конфигурации, загрузим ее
   if (EEPROM.read(CONFIG_EEPROM_ADDR) == CONFIG_VERSION) {
     EEPROM.get(CONFIG_EEPROM_ADDR, config);
   } else {
     // Иначе используем стандартные настройки
     config.configVersion = CONFIG_VERSION;
     strcpy(config.mqttServer, MQTT_SERVER);
     config.mqttPort = MQTT_PORT;
     strcpy(config.mqttUsername, MQTT_USERNAME);
     strcpy(config.mqttPassword, MQTT_PASSWORD);
     strcpy(config.mqttClientId, MQTT_CLIENT_ID);
     config.dhcpEnabled = false;
     
     // Копирование IP-адресов и MAC
     memcpy(config.ip, ip, 4);
     memcpy(config.gateway, gateway, 4);
     memcpy(config.subnet, subnet, 4);
     memcpy(config.dns, dns, 4);
     memcpy(config.mac, mac, 6);
     
     // Инициализация режимов пинов по умолчанию
     for (int i = 0; i < 70; i++) {
       config.pinModes[i] = 0; // 0 - не используется
       strcpy(config.pinLabels[i], "");
     }
     
     // Сохраняем стандартную конфигурацию
     saveConfiguration();
   }
 }
 
 // Функция для сохранения конфигурации в EEPROM
 void saveConfiguration() {
   EEPROM.put(CONFIG_EEPROM_ADDR, config);
   configChanged = false;
 }
 
 // Обработчик входящих MQTT сообщений
 void mqttCallback(char* topic, byte* payload, unsigned int length) {
   // Добавляем нуль-терминатор для строки
   char message[length + 1];
   for (unsigned int i = 0; i < length; i++) {
     message[i] = (char)payload[i];
   }
   message[length] = '\0';
   
   Serial.print(F("Получено сообщение ["));
   Serial.print(topic);
   Serial.print(F("]: "));
   Serial.println(message);
   
   // Обработка команд конфигурации
   if (strcmp(topic, MQTT_CONFIG_TOPIC) == 0) {
     handleConfigCommand(message);
   }
   // Обработка других команд шлюзу
   else if (strcmp(topic, MQTT_COMMAND_TOPIC) == 0) {
     handleGatewayCommand(message);
   }
 }
 
 // Функция для обработки команд конфигурации
 void handleConfigCommand(const char* message) {
   StaticJsonDocument<512> doc;
   DeserializationError error = deserializeJson(doc, message);
   
   if (error) {
     Serial.print(F("deserializeJson() завершился с ошибкой: "));
     Serial.println(error.c_str());
     return;
   }
   
   bool configUpdated = false;
   
   // Обработка обновления пинов
   if (doc.containsKey("pins")) {
     JsonArray pins = doc["pins"];
     for (JsonObject pin : pins) {
       if (pin.containsKey("pin") && pin.containsKey("mode")) {
         int pinNumber = pin["pin"];
         int pinMode = pin["mode"];
         
         if (pinNumber >= 0 && pinNumber < 70) {
           config.pinModes[pinNumber] = pinMode;
           
           if (pin.containsKey("label")) {
             strlcpy(config.pinLabels[pinNumber], pin["label"], 20);
           }
           
           // Установка режима пина
           setPinMode(pinNumber, pinMode);
           configUpdated = true;
         }
       }
     }
   }
   
   // Обновление сетевых настроек
   if (doc.containsKey("network")) {
     JsonObject network = doc["network"];
     if (network.containsKey("dhcp")) {
       config.dhcpEnabled = network["dhcp"];
       configUpdated = true;
     }
     
     if (!config.dhcpEnabled) {
       if (network.containsKey("ip")) {
         JsonArray ipArray = network["ip"];
         for (int i = 0; i < 4 && i < ipArray.size(); i++) {
           config.ip[i] = ipArray[i];
         }
         configUpdated = true;
       }
       
       // Аналогично для gateway, subnet, dns...
     }
   }
   
   // Обновление MQTT настроек
   if (doc.containsKey("mqtt")) {
     JsonObject mqtt = doc["mqtt"];
     if (mqtt.containsKey("server")) {
       strlcpy(config.mqttServer, mqtt["server"], 40);
       configUpdated = true;
     }
     
     if (mqtt.containsKey("port")) {
       config.mqttPort = mqtt["port"];
       configUpdated = true;
     }
     
     if (mqtt.containsKey("username")) {
       strlcpy(config.mqttUsername, mqtt["username"], 20);
       configUpdated = true;
     }
     
     if (mqtt.containsKey("password")) {
       strlcpy(config.mqttPassword, mqtt["password"], 20);
       configUpdated = true;
     }
     
     if (mqtt.containsKey("client_id")) {
       strlcpy(config.mqttClientId, mqtt["client_id"], 30);
       configUpdated = true;
     }
   }
   
   // Если конфигурация была изменена, сохраняем ее
   if (configUpdated) {
     saveConfiguration();
     
     // Отправляем подтверждение
     StaticJsonDocument<256> responseDoc;
     responseDoc["status"] = "ok";
     responseDoc["message"] = "Configuration updated";
     
     char response[256];
     serializeJson(responseDoc, response);
     mqttClient.publish(MQTT_STATUS_TOPIC, response);
     
     // Если сетевые настройки изменились, может потребоваться перезагрузка
     if (doc.containsKey("network")) {
       // TODO: Перезагрузка сетевого соединения
     }
     
     // Если настройки MQTT изменились, переподключаемся
     if (doc.containsKey("mqtt")) {
       reconnectMqtt();
     }
   }
 }
 
 // Функция для обработки основных команд шлюза
 void handleGatewayCommand(const char* message) {
   StaticJsonDocument<256> doc;
   DeserializationError error = deserializeJson(doc, message);
   
   if (error) {
     Serial.print(F("deserializeJson() завершился с ошибкой: "));
     Serial.println(error.c_str());
     return;
   }
   
   // Обработка различных команд шлюзу
   if (doc.containsKey("action")) {
     String action = doc["action"];
     
     if (action == "ping") {
       // Ответ на пинг
       StaticJsonDocument<128> responseDoc;
       responseDoc["status"] = "ok";
       responseDoc["action"] = "pong";
       responseDoc["uptime"] = millis() / 1000;
       
       char response[128];
       serializeJson(responseDoc, response);
       mqttClient.publish(MQTT_STATUS_TOPIC, response);
     } 
     else if (action == "discover") {
       // Запуск процесса обнаружения устройств
       publishDeviceDiscovery();
     }
     else if (action == "restart") {
       // Перезагрузка Arduino (не реализована, т.к. обычно нужен watchdog)
       mqttClient.publish(MQTT_STATUS_TOPIC, "{\"status\":\"restarting\"}");
       // TODO: Реализовать перезагрузку
     }
   }
 }
 
 // Функция для установки режима пина
 void setPinMode(int pin, int mode) {
   switch (mode) {
     case 1: // INPUT
       pinMode(pin, INPUT);
       break;
     case 2: // OUTPUT
       pinMode(pin, OUTPUT);
       break;
     case 3: // INPUT_PULLUP
       pinMode(pin, INPUT_PULLUP);
       break;
     case 4: // ANALOG INPUT (для аналоговых пинов)
       // Для аналоговых пинов не нужно явно устанавливать pinMode
       break;
     case 5: // PWM OUTPUT
       pinMode(pin, OUTPUT);
       break;
     default: // Не используется
       // Не настраиваем пин
       break;
   }
 }
 
 // Публикация обнаружения устройств
 void publishDeviceDiscovery() {
   StaticJsonDocument<512> doc;
   
   doc["name"] = config.mqttClientId;
   doc["model"] = "Arduino Mega Gateway";
   doc["manufacturer"] = "Domovoy";
   doc["sw_version"] = "1.0.0";
   
   JsonArray sensors = doc.createNestedArray("sensors");
   JsonArray switches = doc.createNestedArray("switches");
   
   // Добавляем сенсоры, сконфигурированные в системе
   for (int i = 0; i < 70; i++) {
     if (config.pinModes[i] > 0) {
       if (config.pinModes[i] == 4) { // Аналоговый вход
         JsonObject sensor = sensors.createNestedObject();
         sensor["id"] = String("analog_") + i;
         sensor["name"] = config.pinLabels[i][0] ? config.pinLabels[i] : String("Analog Pin ") + i;
         sensor["type"] = "analog";
         sensor["pin"] = i;
       } else if (config.pinModes[i] == 1 || config.pinModes[i] == 3) { // Цифровой вход
         JsonObject sensor = sensors.createNestedObject();
         sensor["id"] = String("digital_") + i;
         sensor["name"] = config.pinLabels[i][0] ? config.pinLabels[i] : String("Digital Pin ") + i;
         sensor["type"] = "binary";
         sensor["pin"] = i;
       } else if (config.pinModes[i] == 2 || config.pinModes[i] == 5) { // Цифровой или PWM выход
         JsonObject sw = switches.createNestedObject();
         sw["id"] = String("switch_") + i;
         sw["name"] = config.pinLabels[i][0] ? config.pinLabels[i] : String("Switch Pin ") + i;
         sw["type"] = config.pinModes[i] == 5 ? "pwm" : "binary";
         sw["pin"] = i;
       }
     }
   }
   
   char discoveryMessage[512];
   serializeJson(doc, discoveryMessage);
   mqttClient.publish(MQTT_DISCOVERY_PREFIX, discoveryMessage, true); // retained сообщение
 }
 
 // Публикация статуса шлюза
 void publishStatus() {
   StaticJsonDocument<256> doc;
   
   doc["status"] = mqttConnected ? "online" : "connecting";
   doc["uptime"] = millis() / 1000;
   doc["free_memory"] = freeMemory();
   
   char statusMessage[256];
   serializeJson(doc, statusMessage);
   mqttClient.publish(MQTT_STATUS_TOPIC, statusMessage);
 }
 
 // Чтение и публикация данных с сенсоров
 void readAndPublishSensors() {
   for (int i = 0; i < 70; i++) {
     if (config.pinModes[i] == 4) { // Аналоговый вход
       int value = analogRead(i);
       char topic[50];
       sprintf(topic, "domovoy/sensor/analog_%d", i);
       
       StaticJsonDocument<128> doc;
       doc["value"] = value;
       doc["pin"] = i;
       
       char message[128];
       serializeJson(doc, message);
       mqttClient.publish(topic, message);
     } 
     else if (config.pinModes[i] == 1 || config.pinModes[i] == 3) { // Цифровой вход
       int value = digitalRead(i);
       char topic[50];
       sprintf(topic, "domovoy/sensor/digital_%d", i);
       
       StaticJsonDocument<128> doc;
       doc["value"] = value;
       doc["pin"] = i;
       
       char message[128];
       serializeJson(doc, message);
       mqttClient.publish(topic, message);
     }
   }
 }
 
 // Подключение к MQTT брокеру
 boolean reconnectMqtt() {
   if (mqttClient.connect(config.mqttClientId, config.mqttUsername, config.mqttPassword, 
                           MQTT_STATUS_TOPIC, 0, true, "{\"status\":\"offline\"}")) {
     Serial.println(F("Подключено к MQTT брокеру"));
     
     // Публикация статуса "online"
     mqttClient.publish(MQTT_STATUS_TOPIC, "{\"status\":\"online\"}", true);
     
     // Подписка на топики
     mqttClient.subscribe(MQTT_COMMAND_TOPIC);
     mqttClient.subscribe(MQTT_CONFIG_TOPIC);
     
     // Публикация обнаружения устройств
     publishDeviceDiscovery();
     
     return true;
   }
   return false;
 }
 
 // Инициализация сетевого соединения
 void setupNetwork() {
   Serial.println(F("Инициализация сетевого соединения..."));
   
   if (config.dhcpEnabled) {
     // Использование DHCP
     if (Ethernet.begin(config.mac) == 0) {
       Serial.println(F("Не удалось получить IP-адрес по DHCP, использую статический IP"));
       Ethernet.begin(config.mac, IPAddress(config.ip), IPAddress(config.dns), IPAddress(config.gateway), IPAddress(config.subnet));
     }
   } else {
     // Использование статического IP
     Ethernet.begin(config.mac, IPAddress(config.ip), IPAddress(config.dns), IPAddress(config.gateway), IPAddress(config.subnet));
   }
   
   delay(1500); // Даем время на инициализацию Ethernet
   
   Serial.print(F("IP адрес: "));
   Serial.println(Ethernet.localIP());
   
   networkConnected = true;
 }
 
 // Получение свободной памяти
 int freeMemory() {
   extern int __heap_start, *__brkval;
   int v;
   return (int) &v - (__brkval == 0 ? (int) &__heap_start : (int) __brkval);
 }
 
 void setup() {
   // Инициализация последовательного порта
   Serial.begin(115200);
   Serial.println(F("Domovoy Arduino Mega Gateway запускается..."));
   
   // Инициализация индикаторов
   pinMode(LED_STATUS, OUTPUT);
   pinMode(LED_MQTT, OUTPUT);
   digitalWrite(LED_STATUS, HIGH); // Включаем индикатор статуса
   digitalWrite(LED_MQTT, LOW);   // Выключаем индикатор MQTT
   
   // Загрузка конфигурации
   loadConfiguration();
   
   // Инициализация пинов согласно конфигурации
   for (int i = 0; i < 70; i++) {
     if (config.pinModes[i] > 0) {
       setPinMode(i, config.pinModes[i]);
     }
   }
   
   // Инициализация Ethernet соединения
   setupNetwork();
   
   // Настройка MQTT клиента
   mqttClient.setServer(config.mqttServer, config.mqttPort);
   mqttClient.setCallback(mqttCallback);
   
   // Первая попытка подключения к MQTT
   if (mqttClient.connect(config.mqttClientId, config.mqttUsername, config.mqttPassword, 
                          MQTT_STATUS_TOPIC, 0, true, "{\"status\":\"offline\"}")) {
     Serial.println(F("Успешное подключение к MQTT брокеру"));
     mqttConnected = true;
     digitalWrite(LED_MQTT, HIGH);
     
     // Подписка на топики
     mqttClient.subscribe(MQTT_COMMAND_TOPIC);
     mqttClient.subscribe(MQTT_CONFIG_TOPIC);
     
     // Публикация статуса и обнаружения устройств
     mqttClient.publish(MQTT_STATUS_TOPIC, "{\"status\":\"online\"}", true);
     publishDeviceDiscovery();
   } else {
     Serial.println(F("Не удалось подключиться к MQTT брокеру, будет выполнена повторная попытка"));
     mqttConnected = false;
     digitalWrite(LED_MQTT, LOW);
   }
   
   // Инициализация таймеров
   lastReconnectAttempt = 0;
   lastStatusUpdate = 0;
   lastSensorUpdate = 0;
   
   digitalWrite(LED_STATUS, LOW); // Выключаем индикатор статуса, инициализация завершена
 }
 
 void loop() {
   // Текущее время
   unsigned long currentMillis = millis();
   
   // Обработка MQTT соединения
   if (!mqttClient.connected()) {
     mqttConnected = false;
     digitalWrite(LED_MQTT, LOW);
     
     // Повторное подключение к MQTT
     if (currentMillis - lastReconnectAttempt > RECONNECT_INTERVAL) {
       lastReconnectAttempt = currentMillis;
       
       // Попытка переподключения
       if (reconnectMqtt()) {
         mqttConnected = true;
         digitalWrite(LED_MQTT, HIGH);
         lastReconnectAttempt = 0;
       }
     }
   } else {
     // Обработка входящих сообщений
     mqttClient.loop();
     mqttConnected = true;
   }
   
   // Периодическое обновление статуса
   if (mqttConnected && (currentMillis - lastStatusUpdate > STATUS_INTERVAL)) {
     lastStatusUpdate = currentMillis;
     publishStatus();
     
     // Мигаем светодиодом статуса
     digitalWrite(LED_STATUS, HIGH);
     delay(50);
     digitalWrite(LED_STATUS, LOW);
   }
   
   // Периодическое считывание и публикация данных с сенсоров
   if (mqttConnected && (currentMillis - lastSensorUpdate > SENSOR_READ_INTERVAL)) {
     lastSensorUpdate = currentMillis;
     readAndPublishSensors();
   }
   
   // Проверка необходимости сохранения конфигурации
   if (configChanged) {
     saveConfiguration();
   }
   
   // Обработка сетевых пакетов для поддержания соединения
   Ethernet.maintain();
 }