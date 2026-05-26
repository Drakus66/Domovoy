# Design Document

## Overview

WebUI - это современное одностраничное приложение (SPA) на React для управления системой умного дома Domovoy. Приложение взаимодействует с бэкендом через REST API шлюза ApiGateway, который проксирует запросы к микросервисам. Интерфейс предоставляет интуитивный способ просмотра устройств, управления ими в реальном времени и анализа исторических данных датчиков.

Первая версия WebUI ориентирована на функциональность админ-панели для настройки системы и отладки. Интерфейс использует polling с batching (0.5-1 секунда) для обновления данных вместо WebSocket соединений. Авторизация не требуется на начальном этапе.

## Architecture

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────┐
│                      Browser                            │
│                                                         │
│  ┌───────────────────────────────────────────────────┐  │
│  │           React WebUI Application                 │  │
│  │                                                   │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────────┐   │  │
│  │  │Dashboard │  │  Logs    │  │  Components  │   │  │
│  │  │  Page    │  │  Page    │  │   Library    │   │  │
│  │  └──────────┘  └──────────┘  └──────────────┘   │  │
│  │                                                   │  │
│  │  ┌────────────────────────────────────────────┐  │  │
│  │  │    State Management (Zustand)              │  │  │
│  │  └────────────────────────────────────────────┘  │  │
│  │  ┌────────────────────────────────────────────┐  │  │
│  │  │    API Client + Polling (Axios)            │  │  │
│  │  └────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
                          │
                          │ HTTP/REST
                          │ Polling (0.5-1s)
                          ▼
┌─────────────────────────────────────────────────────────┐
│                   ApiGateway                            │
│                     (Ocelot)                            │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│              Backend Microservices                      │
│  ┌──────────┐  ┌──────────┐  ┌──────────────┐          │
│  │DbGateway │  │  Device  │  │    Sensor    │          │
│  │          │  │ Service  │  │   Service    │          │
│  └──────────┘  └──────────┘  └──────────────┘          │
└─────────────────────────────────────────────────────────┘
```

### Technology Stack

- **Framework**: React 18+ with TypeScript
- **Build Tool**: Vite
- **State Management**: Zustand for global state
- **HTTP Client**: Axios
- **Data Fetching**: Polling with batching (0.5-1s intervals)
- **Routing**: React Router v6
- **UI Components**: Material-UI (MUI) v5
- **Charts**: Recharts
- **Form Handling**: React Hook Form
- **Styling**: MUI styled components + CSS Modules
- **Testing**: Vitest + React Testing Library
- **Property Testing**: fast-check

### Project Structure

```
src/UI/WebUI/
├── public/
│   └── index.html
├── src/
│   ├── api/
│   │   ├── client.ts              # Axios instance configuration
│   │   ├── devices.ts             # Device API calls
│   │   ├── sensors.ts             # Sensor API calls
│   │   ├── logs.ts                # Logs API calls
│   │   └── polling.ts             # Polling service with batching
│   ├── components/
│   │   ├── common/
│   │   │   ├── Loading.tsx
│   │   │   ├── ErrorBoundary.tsx
│   │   │   └── Notification.tsx
│   │   ├── devices/
│   │   │   ├── DeviceCard.tsx
│   │   │   ├── LightControl.tsx
│   │   │   ├── SensorDisplay.tsx
│   │   │   └── DeviceGrid.tsx
│   │   ├── charts/
│   │   │   └── SensorChart.tsx
│   │   └── layout/
│   │       ├── Navigation.tsx
│   │       └── Layout.tsx
│   ├── hooks/
│   │   ├── useDevices.ts
│   │   ├── useSensors.ts
│   │   ├── useDeviceCommand.ts
│   │   └── usePolling.ts
│   ├── pages/
│   │   ├── Dashboard.tsx
│   │   └── Logs.tsx
│   ├── store/
│   │   ├── deviceStore.ts         # Zustand store for devices
│   │   ├── sensorStore.ts         # Zustand store for sensors
│   │   └── uiStore.ts             # Zustand store for UI state
│   ├── types/
│   │   ├── device.ts
│   │   ├── sensor.ts
│   │   └── api.ts
│   ├── utils/
│   │   ├── formatters.ts
│   │   └── constants.ts
│   ├── App.tsx
│   ├── main.tsx
│   └── theme.ts
├── package.json
├── tsconfig.json
├── vite.config.ts
└── README.md
```

## Components and Interfaces

### Core Components

#### 1. Layout Components

**Navigation Component**
- Provides top-level navigation between Dashboard and Logs
- Displays current page indicator
- Responsive hamburger menu for mobile

**Layout Component**
- Wraps all pages with consistent header and navigation
- Provides error boundary
- Manages global notification system

#### 2. Device Components

**DeviceCard Component**
```typescript
interface DeviceCardProps {
  device: Device;
  onCommand: (deviceId: string, command: DeviceCommand) => Promise<void>;
}
```
- Displays device information (name, type, status, last seen)
- Shows online/offline indicator
- Renders appropriate control based on device type
- Handles loading and error states

**LightControl Component**
```typescript
interface LightControlProps {
  device: Device;
  light: Light;
  onToggle: (deviceId: string, state: boolean) => Promise<void>;
  onBrightnessChange: (deviceId: string, brightness: number) => Promise<void>;
}
```
- Toggle switch for on/off control
- Brightness slider for dimmable lights
- Debounced brightness updates
- Visual feedback during command execution

**SensorDisplay Component**
```typescript
interface SensorDisplayProps {
  device: Device;
  sensor: Sensor;
  readings: SensorReading[];
}
```
- Displays current sensor value with units
- Shows data freshness indicator
- Handles multiple sensor types (temperature, humidity, etc.)
- Formats values with appropriate precision

**DeviceGrid Component**
```typescript
interface DeviceGridProps {
  devices: Device[];
  onDeviceCommand: (deviceId: string, command: DeviceCommand) => Promise<void>;
}
```
- Responsive grid layout for device cards
- Handles empty state
- Provides loading skeleton
- Groups devices by location (optional)

#### 3. Chart Components

**SensorChart Component**
```typescript
interface SensorChartProps {
  sensorId: string;
  timeRange: TimeRange;
  sensorType: string;
}
```
- Line chart for historical sensor data
- Time range selector (1h, 6h, 24h, 7d)
- Responsive chart sizing
- Loading and error states
- Tooltip with formatted values

#### 4. Common Components

**Loading Component**
- Circular progress indicator
- Skeleton loaders for cards

**ErrorBoundary Component**
- Catches React errors
- Displays user-friendly error message
- Provides retry mechanism

**Notification Component**
- Toast notifications for success/error
- Auto-dismiss after timeout
- Stacked notifications support

### Pages

#### Dashboard Page
- Fetches and displays all devices
- Automatic updates via polling (0.5-1s)
- Device filtering by type/location
- Responsive grid layout

#### Logs Page
- Displays system logs in table format
- Log level filtering (info, warning, error)
- Pagination or infinite scroll
- Auto-refresh capability
- Search functionality

## Data Models

### TypeScript Interfaces

```typescript
// Device Types
interface Device {
  deviceId: string;
  name: string;
  type: DeviceType;
  locationId: string;
  status: DeviceStatus;
  isOnline: boolean;
  lastSeen: Date;
  configuration: Record<string, any>;
}

type DeviceType = 'Light' | 'Sensor' | 'Switch' | 'Thermostat' | 'Unknown';
type DeviceStatus = 'Online' | 'Offline' | 'Error';

interface Light {
  lightId: string;
  deviceId: string;
  dimmable: boolean;
  colorSupport: boolean;
  defaultBrightness: number;
}

interface Sensor {
  sensorId: string;
  deviceId: string;
  type: SensorType;
  updateFrequency: number;
  precision: number;
  lastValue: number | null;
}

type SensorType = 'Temperature' | 'Humidity' | 'Motion' | 'Light' | 'Pressure';

interface SensorReading {
  sensorId: string;
  timestamp: Date;
  value: number;
  unit: string;
}

// Command Types
interface DeviceCommand {
  deviceId: string;
  command: string;
  parameters: Record<string, any>;
}

interface LightCommand extends DeviceCommand {
  command: 'toggle' | 'setBrightness' | 'setColor';
  parameters: {
    state?: boolean;
    brightness?: number;
    color?: string;
  };
}

// Log Types
interface LogEntry {
  id: string;
  timestamp: Date;
  level: LogLevel;
  source: string;
  message: string;
  details?: Record<string, any>;
}

type LogLevel = 'info' | 'warning' | 'error' | 'debug';

// API Response Types
interface ApiResponse<T> {
  data: T;
  success: boolean;
  message?: string;
}

interface PaginatedResponse<T> {
  data: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
```

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system-essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*


### Property Reflection

After reviewing all testable properties from the prework analysis, the following consolidations were identified:

- Properties 1.1 and 1.2 can be combined: both test device card rendering with all required information
- Properties 2.2 and 2.4 follow the same pattern: user interaction triggers API call with correct parameters
- Properties 8.1, 8.2, 8.3, and 8.4 all test command execution feedback and can be consolidated into comprehensive command handling properties
- Properties 9.1, 9.2, and 9.3 all test API request formatting and can be verified through a single API client property

### Core Properties

Property 1: Device card rendering completeness
*For any* device, when rendered as a card, the output should contain the device name, type, status, and online indicator
**Validates: Requirements 1.2**

Property 2: Automatic data refresh via polling
*For any* polling interval, the application should fetch updated device and sensor data at the configured interval (0.5-1s)
**Validates: Requirements 3.2**

Property 3: Light control presence
*For any* light device, when rendered, the card should include a toggle switch control
**Validates: Requirements 2.1**

Property 3: Dimmable light slider presence
*For any* dimmable light device, when rendered, the card should include a brightness slider control
**Validates: Requirements 2.3**

Property 4: Device command API call correctness
*For any* device command (toggle, brightness, etc.), executing the command should trigger an API call with the correct endpoint, method, and payload structure
**Validates: Requirements 2.2, 2.4, 9.2**

Property 5: Command failure state reversion
*For any* device command that fails, the UI state should revert to the previous state before the command was initiated
**Validates: Requirements 2.5**

Property 6: Sensor reading display
*For any* sensor device, when rendered, the card should display the current sensor value with appropriate units
**Validates: Requirements 3.1**

Property 7: Multi-value sensor display
*For any* sensor with multiple reading types, all reading values should be displayed in the rendered output
**Validates: Requirements 3.3**

Property 8: Sensor value formatting
*For any* sensor reading value, the displayed number should be formatted with the precision specified by the sensor configuration
**Validates: Requirements 3.5**

Property 9: Stale data indication
*For any* sensor reading where the timestamp is older than the update frequency threshold, the UI should display a visual indicator of stale data
**Validates: Requirements 3.4**

Property 10: Historical data API request
*For any* time range selection, fetching historical sensor data should generate an API request with the correct start and end time parameters
**Validates: Requirements 4.3**

Property 11: Sensor type filtering
*For any* sensor type filter selection, the displayed sensors should only include sensors matching the selected type
**Validates: Requirements 4.4**

Property 12: Log entry display completeness
*For any* log entry, when rendered, the output should contain timestamp, log level, and message
**Validates: Requirements 5.2**

Property 13: Log level filtering
*For any* log level filter selection, the displayed logs should only include entries matching the selected level
**Validates: Requirements 5.4**

Property 14: Pagination correctness
*For any* page number selection in paginated data, the displayed items should correspond to the correct slice of the total dataset
**Validates: Requirements 5.5**

Property 15: Navigation route synchronization
*For any* navigation action, the browser URL should update to reflect the current page route
**Validates: Requirements 6.2, 6.5**

Property 16: Active navigation highlighting
*For any* current route, the corresponding navigation menu item should be marked as active
**Validates: Requirements 6.3**

Property 17: Command loading state
*For any* device command in progress, the associated control should display a loading indicator and be disabled
**Validates: Requirements 8.1, 8.4**

Property 18: Command success notification
*For any* successful device command, a success notification should be displayed to the user
**Validates: Requirements 8.2**

Property 19: Command error notification
*For any* failed device command, an error notification with a descriptive message should be displayed
**Validates: Requirements 8.3**

Property 20: API request structure
*For any* API call, the request should use the correct HTTP method, endpoint path, and include required headers
**Validates: Requirements 9.1, 9.2, 9.3**

Property 21: Network error handling
*For any* API request that encounters a network error or timeout, the error should be caught and handled gracefully without crashing the application
**Validates: Requirements 9.5**

Property 22: Device type icon mapping
*For any* device type, the rendered card should display the icon corresponding to that device type
**Validates: Requirements 10.2**

## Error Handling

### API Error Handling

**Network Errors**
- Implement axios interceptors for global error handling
- Retry failed requests with exponential backoff (max 3 retries)
- Display user-friendly error messages
- Provide manual retry option for critical operations

**HTTP Error Codes**
- 400 Bad Request: Show validation error details
- 401 Unauthorized: Redirect to login (future feature)
- 403 Forbidden: Show permission denied message
- 404 Not Found: Show resource not found message
- 500 Server Error: Show generic server error with retry option
- 503 Service Unavailable: Show service unavailable message

**Timeout Handling**
- Set request timeout to 30 seconds
- Show timeout error with retry option
- Cancel pending requests on component unmount

### Component Error Handling

**Error Boundaries**
- Wrap main application in error boundary
- Catch and log React errors
- Display fallback UI with error details
- Provide "Reload" button to recover

**Validation Errors**
- Validate user input before sending commands
- Show inline validation errors
- Prevent invalid commands from being sent

### Real-time Connection Errors

**Polling Service**
- Handle network failures gracefully during polling
- Implement exponential backoff on repeated failures
- Show connection status indicator
- Pause polling when tab is not visible (Page Visibility API)

## Testing Strategy

### Unit Testing

**Component Tests**
- Test component rendering with various props
- Test user interactions (clicks, input changes)
- Test conditional rendering logic
- Test error states and loading states
- Use React Testing Library for DOM queries
- Mock API calls and external dependencies

**Hook Tests**
- Test custom hooks in isolation
- Test state management logic
- Test side effects and cleanup
- Use @testing-library/react-hooks

**Utility Function Tests**
- Test formatters (date, number, unit conversion)
- Test validation functions
- Test data transformation utilities

### Property-Based Testing

Property-based tests will be implemented using fast-check library for JavaScript/TypeScript. Each test should run a minimum of 100 iterations to ensure comprehensive coverage across the input space.

**Test Tagging Convention**
Each property-based test must include a comment tag in this exact format:
```typescript
// Feature: web-ui, Property {number}: {property_text}
```

**Property Test Implementation**
- Each correctness property from the design document must be implemented as a single property-based test
- Tests should generate random valid inputs using fast-check arbitraries
- Tests should verify the property holds for all generated inputs
- Tests should be placed close to the implementation they verify

**Generator Strategy**
- Create custom arbitraries for domain types (Device, Sensor, Light)
- Use constraints to generate valid data (e.g., brightness 0-100)
- Generate edge cases (empty strings, null values, boundary values)
- Combine arbitraries for complex scenarios

### API Integration

**API Integration**
- Test API client with mock server (MSW - Mock Service Worker)
- Test request/response handling
- Test error scenarios
- Test retry logic
- Test polling service with batching

**End-to-End Testing**
- Test critical user flows (view devices, control light, view charts)
- Test navigation between pages
- Test real-time updates
- Use Playwright or Cypress for E2E tests

### Testing Configuration

**Vitest Configuration**
```typescript
// vite.config.ts
export default defineConfig({
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      exclude: ['node_modules/', 'src/test/']
    }
  }
});
```

**Test Setup**
- Configure React Testing Library
- Setup MSW for API mocking
- Configure fast-check for property testing
- Setup test utilities and helpers

## API Integration

### REST API Endpoints

**Device Endpoints**
```
GET    /api/devices              # Get all devices
GET    /api/devices/{id}         # Get device by ID
GET    /api/devices/type/{type}  # Get devices by type
GET    /api/devices/location/{location}  # Get devices by location
POST   /api/devices              # Create device
PUT    /api/devices/{id}         # Update device
PATCH  /api/devices/{id}/state   # Update device state
DELETE /api/devices/{id}         # Delete device
```

**Sensor Endpoints**
```
GET    /api/sensors                    # Get all sensors
GET    /api/sensors/{id}               # Get sensor by ID
GET    /api/sensors/{id}/readings      # Get sensor readings
GET    /api/sensors/{id}/history       # Get historical data
  ?start={timestamp}&end={timestamp}   # Time range parameters
```

**Device Control Endpoints**
```
POST   /api/device-control/command     # Send device command
  Body: { deviceId, command, parameters }
```

**Logs Endpoints** (Future)
```
GET    /api/logs                       # Get system logs
  ?level={level}&page={page}&size={size}
```

### Polling Strategy

**Batched Polling**
- Poll interval: 500-1000ms (configurable)
- Batch multiple device/sensor requests into single calls
- Use conditional requests (If-Modified-Since, ETag) to reduce bandwidth
- Pause polling when page is hidden (Page Visibility API)
- Resume polling when page becomes visible

**Polling Implementation**
```typescript
// Zustand store with polling
const useDeviceStore = create((set, get) => ({
  devices: [],
  isPolling: false,
  pollInterval: 1000,
  
  startPolling: () => {
    const poll = async () => {
      if (!document.hidden) {
        const devices = await fetchDevices();
        set({ devices });
      }
    };
    
    const intervalId = setInterval(poll, get().pollInterval);
    set({ isPolling: true, intervalId });
  },
  
  stopPolling: () => {
    clearInterval(get().intervalId);
    set({ isPolling: false });
  }
}));
```

### API Client Implementation

**Axios Configuration**
```typescript
const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000',
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json'
  }
});

// Request interceptor
apiClient.interceptors.request.use(
  (config) => {
    // Future: Add auth token if available
    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor
apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    // Handle errors globally
    return Promise.reject(error);
  }
);
```

**Zustand Store Configuration**
```typescript
// Device store with polling
const useDeviceStore = create<DeviceStore>((set, get) => ({
  devices: [],
  loading: false,
  error: null,
  pollInterval: 1000,
  intervalId: null,
  
  fetchDevices: async () => {
    try {
      const response = await apiClient.get('/api/devices');
      set({ devices: response.data, error: null });
    } catch (error) {
      set({ error: error.message });
    }
  },
  
  startPolling: () => {
    const { fetchDevices, pollInterval } = get();
    fetchDevices(); // Initial fetch
    
    const id = setInterval(() => {
      if (!document.hidden) {
        fetchDevices();
      }
    }, pollInterval);
    
    set({ intervalId: id });
  },
  
  stopPolling: () => {
    const { intervalId } = get();
    if (intervalId) {
      clearInterval(intervalId);
      set({ intervalId: null });
    }
  }
}));
```

## Performance Considerations

### Optimization Strategies

**Code Splitting**
- Lazy load pages with React.lazy()
- Split vendor bundles
- Dynamic imports for heavy components (charts)

**Memoization**
- Use React.memo for expensive components
- Use useMemo for expensive calculations
- Use useCallback for event handlers passed to children

**Virtual Scrolling**
- Implement virtual scrolling for large device lists
- Use react-window or react-virtualized

**Debouncing**
- Debounce brightness slider updates (300ms)
- Debounce search/filter inputs (500ms)

**Caching**
- Cache API responses in Zustand stores
- Implement stale-while-revalidate pattern with polling
- Use optimistic updates for commands

**Bundle Size**
- Tree-shake unused code
- Use production builds
- Compress assets with gzip/brotli
- Analyze bundle with vite-bundle-visualizer

### Performance Metrics

**Target Metrics**
- First Contentful Paint (FCP): < 1.5s
- Time to Interactive (TTI): < 3.5s
- Largest Contentful Paint (LCP): < 2.5s
- Cumulative Layout Shift (CLS): < 0.1
- First Input Delay (FID): < 100ms

## Security Considerations

### Input Validation
- Validate all user inputs client-side
- Sanitize data before display
- Prevent XSS attacks

### HTTPS
- Enforce HTTPS in production
- Secure API connections

### Content Security Policy
- Implement CSP headers
- Restrict inline scripts
- Whitelist trusted sources

**Note**: Authentication and authorization will be implemented in future phases.

## Deployment

### Build Process

**Development Build**
```bash
npm run dev
```

**Production Build**
```bash
npm run build
```
- Minifies JavaScript and CSS
- Optimizes images
- Generates source maps
- Outputs to `dist/` directory

### Static File Serving

The built application will be served by ApiGateway from the `wwwroot/` directory:
- Copy build output to `src/Gateway/Domovoy.ApiGateway/wwwroot/`
- ApiGateway serves static files with `app.UseStaticFiles()`
- SPA fallback configured with `app.UseDefaultFiles()`

### Environment Variables

```env
VITE_API_BASE_URL=http://localhost:5000
VITE_POLL_INTERVAL=1000
VITE_APP_VERSION=1.0.0
```

### Docker Integration (Future)

Multi-stage Dockerfile for WebUI:
```dockerfile
FROM node:18-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/nginx.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

## Accessibility

### WCAG 2.1 Compliance

**Keyboard Navigation**
- All interactive elements accessible via keyboard
- Logical tab order
- Visible focus indicators

**Screen Reader Support**
- Semantic HTML elements
- ARIA labels for custom controls
- ARIA live regions for dynamic updates

**Color Contrast**
- Minimum 4.5:1 contrast ratio for text
- 3:1 for large text and UI components

**Responsive Text**
- Support browser zoom up to 200%
- Relative font sizes (rem/em)

### Accessibility Testing
- Use axe-core for automated testing
- Manual keyboard navigation testing
- Screen reader testing (NVDA, JAWS, VoiceOver)

## Future Enhancements

### Phase 2 Features
- User authentication and authorization (JWT)
- WebSocket/SignalR for real-time updates
- Device grouping and scenes
- Automation rules editor
- Push notifications
- Mobile app (React Native)

### Phase 3 Features
- Voice control integration
- Advanced analytics dashboard
- Energy consumption tracking
- Multi-user support with permissions
- Offline mode with service workers
- Progressive Web App (PWA) capabilities
