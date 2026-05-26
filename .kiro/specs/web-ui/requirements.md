# Requirements Document

## Introduction

WebUI - это пользовательский веб-интерфейс для системы управления умным домом Domovoy. Приложение построено на React и взаимодействует с бэкендом через REST API шлюза ApiGateway. Интерфейс предоставляет пользователям возможность просматривать устройства, управлять ими и отслеживать историю показаний датчиков.

## Glossary

- **WebUI**: Веб-приложение на React для управления умным домом
- **ApiGateway**: REST API шлюз для взаимодействия с микросервисами бэкенда
- **Device**: Устройство умного дома (датчик, светильник, переключатель и т.д.)
- **Device Card**: Визуальный компонент, отображающий информацию об устройстве
- **Control Element**: Элемент управления устройством (переключатель, слайдер, регулятор)
- **Sensor Reading**: Показание датчика с временной меткой
- **Dashboard**: Главная страница с карточками устройств
- **Logs View**: Страница для просмотра логов и системной информации

## Requirements

### Requirement 1

**User Story:** Как пользователь, я хочу видеть список всех моих устройств умного дома, чтобы иметь общее представление о системе.

#### Acceptance Criteria

1. WHEN the user opens the Dashboard THEN the WebUI SHALL display all registered devices as cards
2. WHEN displaying device cards THEN the WebUI SHALL show device name, type, and current status for each device
3. WHEN the device list is empty THEN the WebUI SHALL display a message indicating no devices are available
4. WHEN device data is loading THEN the WebUI SHALL display a loading indicator
5. WHEN the ApiGateway is unavailable THEN the WebUI SHALL display an error message and provide a retry option

### Requirement 2

**User Story:** Как пользователь, я хочу управлять устройствами освещения, чтобы включать/выключать свет и регулировать яркость.

#### Acceptance Criteria

1. WHEN a light device card is displayed THEN the WebUI SHALL provide a toggle switch for on/off control
2. WHEN the user toggles the light switch THEN the WebUI SHALL send a command to the ApiGateway and update the device state
3. WHEN a dimmable light is displayed THEN the WebUI SHALL provide a slider control for brightness adjustment
4. WHEN the user adjusts the brightness slider THEN the WebUI SHALL send the brightness value to the ApiGateway
5. WHEN a command fails THEN the WebUI SHALL display an error notification and revert the UI state

### Requirement 3

**User Story:** Как пользователь, я хочу видеть текущие показания датчиков, чтобы отслеживать параметры окружающей среды.

#### Acceptance Criteria

1. WHEN a sensor device card is displayed THEN the WebUI SHALL show the current sensor reading with units
2. WHEN sensor readings are updated THEN the WebUI SHALL refresh the displayed values automatically
3. WHEN a sensor has multiple readings (temperature, humidity) THEN the WebUI SHALL display all values clearly
4. WHEN a sensor reading is stale or unavailable THEN the WebUI SHALL indicate the data status visually
5. WHEN displaying sensor values THEN the WebUI SHALL format numbers appropriately with proper decimal places

### Requirement 4

**User Story:** Как пользователь, я хочу просматривать историю показаний датчиков в виде графиков, чтобы анализировать изменения во времени.

#### Acceptance Criteria

1. WHEN the user selects a sensor device THEN the WebUI SHALL display a historical data chart
2. WHEN displaying historical data THEN the WebUI SHALL show time on the X-axis and sensor values on the Y-axis
3. WHEN the user requests historical data THEN the WebUI SHALL fetch data from the ApiGateway for the specified time range
4. WHEN multiple sensor types exist THEN the WebUI SHALL allow filtering by sensor type
5. WHEN historical data is unavailable THEN the WebUI SHALL display an appropriate message

### Requirement 5

**User Story:** Как пользователь, я хочу просматривать системные логи и информацию, чтобы диагностировать проблемы и отслеживать активность системы.

#### Acceptance Criteria

1. WHEN the user navigates to the Logs View THEN the WebUI SHALL display a list of system logs
2. WHEN displaying logs THEN the WebUI SHALL show timestamp, log level, and message for each entry
3. WHEN new log entries are available THEN the WebUI SHALL update the log list automatically
4. WHEN the user filters logs THEN the WebUI SHALL allow filtering by log level (info, warning, error)
5. WHEN displaying logs THEN the WebUI SHALL provide pagination or infinite scroll for large log sets

### Requirement 6

**User Story:** Как пользователь, я хочу иметь интуитивную навигацию между разделами приложения, чтобы легко находить нужную информацию.

#### Acceptance Criteria

1. WHEN the WebUI loads THEN the system SHALL display a navigation menu with Dashboard and Logs sections
2. WHEN the user clicks a navigation item THEN the WebUI SHALL navigate to the corresponding page without full page reload
3. WHEN navigating between pages THEN the WebUI SHALL highlight the active navigation item
4. WHEN on mobile devices THEN the WebUI SHALL provide a responsive navigation menu
5. WHEN the user is on a specific page THEN the WebUI SHALL update the browser URL to reflect the current location

### Requirement 7

**User Story:** Как пользователь, я хочу, чтобы интерфейс был отзывчивым и работал на различных устройствах, чтобы управлять домом с любого устройства.

#### Acceptance Criteria

1. WHEN the WebUI is accessed from a desktop THEN the system SHALL display a multi-column layout for device cards
2. WHEN the WebUI is accessed from a tablet THEN the system SHALL adjust the layout to a two-column grid
3. WHEN the WebUI is accessed from a mobile phone THEN the system SHALL display a single-column layout
4. WHEN the viewport size changes THEN the WebUI SHALL adapt the layout responsively
5. WHEN touch gestures are available THEN the WebUI SHALL support touch interactions for controls

### Requirement 8

**User Story:** Как пользователь, я хочу получать визуальную обратную связь при взаимодействии с устройствами, чтобы понимать, что мои действия обрабатываются.

#### Acceptance Criteria

1. WHEN the user initiates a device command THEN the WebUI SHALL display a loading indicator on the affected control
2. WHEN a command succeeds THEN the WebUI SHALL show a success notification or visual confirmation
3. WHEN a command fails THEN the WebUI SHALL display an error notification with a descriptive message
4. WHEN the WebUI is processing a request THEN the system SHALL disable the control to prevent duplicate commands
5. WHEN notifications are displayed THEN the WebUI SHALL automatically dismiss them after a few seconds

### Requirement 9

**User Story:** Как разработчик, я хочу, чтобы WebUI взаимодействовал с ApiGateway через стандартизированные REST API, чтобы обеспечить надежную интеграцию.

#### Acceptance Criteria

1. WHEN fetching device list THEN the WebUI SHALL send a GET request to the ApiGateway devices endpoint
2. WHEN sending device commands THEN the WebUI SHALL send POST requests with proper JSON payload to the ApiGateway
3. WHEN fetching sensor history THEN the WebUI SHALL send GET requests with time range parameters to the ApiGateway
4. WHEN API requests fail THEN the WebUI SHALL implement retry logic with exponential backoff
5. WHEN making API calls THEN the WebUI SHALL include proper error handling for network failures and timeouts

### Requirement 10

**User Story:** Как пользователь, я хочу, чтобы интерфейс имел современный и чистый дизайн, чтобы приятно было им пользоваться.

#### Acceptance Criteria

1. WHEN the WebUI is displayed THEN the system SHALL use a consistent color scheme and typography
2. WHEN displaying device cards THEN the WebUI SHALL use appropriate icons for different device types
3. WHEN showing interactive elements THEN the WebUI SHALL provide hover and focus states for accessibility
4. WHEN the interface loads THEN the WebUI SHALL use smooth transitions and animations for state changes
5. WHEN displaying content THEN the WebUI SHALL maintain adequate spacing and visual hierarchy
