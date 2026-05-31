# Implementation Plan

- [x] 1. Initialize React project with Vite and TypeScript



  - Create new Vite project with React-TS template in `src/UI/WebUI`
  - Configure TypeScript with strict mode
  - Setup project structure (folders: api, components, hooks, pages, store, types, utils)
  - Install core dependencies: React, React Router, Axios, Zustand, MUI
  - Configure Vite for development and production builds
  - _Requirements: All_

- [x] 2. Setup development infrastructure
  - [x] 2.1 Configure Material-UI theme
    - Create theme.ts with color palette and typography
    - Setup ThemeProvider in App.tsx
    - Define responsive breakpoints
    - _Requirements: 10.1_
  
  - [x] 2.2 Setup routing with React Router
    - Install react-router-dom
    - Create route configuration for Dashboard and Logs pages
    - Implement Layout component with navigation
    - _Requirements: 6.1, 6.2_
  
  - [x] 2.3 Configure Axios API client
    - Create api/client.ts with base configuration
    - Setup request/response interceptors
    - Configure timeout and error handling
    - _Requirements: 9.1, 9.5_
  
  - [x] 2.4 Setup testing framework
    - Install Vitest, React Testing Library, and fast-check
    - Configure vitest.config.ts
    - Create test setup file with MSW configuration
    - _Requirements: All_

- [x] 3. Implement TypeScript type definitions
  - [x] 3.1 Define device types
    - Create types/device.ts with Device, Light, DeviceType, DeviceStatus interfaces
    - Define DeviceCommand and LightCommand types
    - _Requirements: 1.1, 2.1_
  
  - [x] 3.2 Define sensor types
    - Create types/sensor.ts with Sensor, SensorReading, SensorType interfaces
    - _Requirements: 3.1, 4.1_
  
  - [x] 3.3 Define API response types
    - Create types/api.ts with ApiResponse, PaginatedResponse interfaces
    - Define error types
    - _Requirements: 9.1_
  
  - [x] 3.4 Define log types
    - Create types/log.ts with LogEntry and LogLevel types
    - _Requirements: 5.1_

- [x] 4. Implement Zustand stores
  - [x] 4.1 Create device store
    - Implement store/deviceStore.ts with device state management
    - Add actions: fetchDevices, updateDevice, sendCommand
    - Implement polling logic with startPolling/stopPolling
    - Handle loading and error states
    - _Requirements: 1.1, 2.2, 3.2_
  
  - [ ]* 4.2 Write property test for device store
    - **Property 1: Device card rendering completeness**
    - **Validates: Requirements 1.2**
  
  - [x] 4.3 Create sensor store
    - Implement store/sensorStore.ts with sensor state management
    - Add actions: fetchSensors, fetchSensorHistory
    - Implement polling for sensor readings
    - _Requirements: 3.1, 4.3_
  
  - [ ]* 4.4 Write property test for sensor store
    - **Property 6: Sensor reading display**
    - **Validates: Requirements 3.1**
  
  - [x] 4.5 Create UI store
    - Implement store/uiStore.ts for notifications and loading states
    - Add actions: showNotification, dismissNotification, setLoading
    - _Requirements: 8.2, 8.3_

- [x] 5. Implement API service layer
  - [x] 5.1 Create devices API service
    - Implement api/devices.ts with getAllDevices, getDeviceById, updateDeviceState
    - Add sendDeviceCommand function
    - _Requirements: 1.1, 2.2, 9.1_
  
  - [ ]* 5.2 Write property test for device API
    - **Property 4: Device command API call correctness**
    - **Validates: Requirements 2.2, 2.4, 9.2**
  
  - [x] 5.3 Create sensors API service
    - Implement api/sensors.ts with getAllSensors, getSensorReadings, getSensorHistory
    - Add time range parameter handling
    - _Requirements: 3.1, 4.3, 9.3**
  
  - [ ]* 5.4 Write property test for sensor API
    - **Property 10: Historical data API request**
    - **Validates: Requirements 4.3**
  
  - [x] 5.5 Create logs API service
    - Implement api/logs.ts with getLogs function
    - Add pagination and filtering parameters
    - _Requirements: 5.1, 5.4_
  
  - [x] 5.6 Implement polling service
    - Create api/polling.ts with batched polling logic
    - Implement Page Visibility API integration
    - Add exponential backoff for failures
    - _Requirements: 3.2, 9.4_
  
  - [ ]* 5.7 Write property test for polling service
    - **Property 2: Automatic data refresh via polling**
    - **Validates: Requirements 3.2**

- [x] 6. Implement common components
  - [x] 6.1 Create Loading component
    - Implement components/common/Loading.tsx with circular progress
    - Create skeleton loaders for cards
    - _Requirements: 1.4_
  
  - [x] 6.2 Create ErrorBoundary component
    - Implement components/common/ErrorBoundary.tsx
    - Add error display and retry mechanism
    - _Requirements: 1.5_
  
  - [x] 6.3 Create Notification component
    - Implement components/common/Notification.tsx with toast notifications
    - Add auto-dismiss functionality
    - Support success, error, and info types
    - _Requirements: 8.2, 8.3, 8.5_
  
  - [ ]* 6.4 Write property test for notification component
    - **Property 18: Command success notification**
    - **Property 19: Command error notification**
    - **Validates: Requirements 8.2, 8.3**

- [x] 7. Implement layout components
  - [x] 7.1 Create Navigation component
    - Implement components/layout/Navigation.tsx with menu items
    - Add active route highlighting
    - Implement responsive mobile menu
    - _Requirements: 6.1, 6.2, 6.3, 6.4_
  
  - [ ]* 7.2 Write property test for navigation
    - **Property 15: Navigation route synchronization**
    - **Property 16: Active navigation highlighting**
    - **Validates: Requirements 6.2, 6.3, 6.5**
  
  - [x] 7.3 Create Layout component
    - Implement components/layout/Layout.tsx wrapping pages
    - Integrate Navigation component
    - Add ErrorBoundary wrapper
    - _Requirements: 6.1_

- [x] 8. Implement device components
  - [x] 8.1 Create DeviceCard component
    - Implement components/devices/DeviceCard.tsx
    - Display device name, type, status, online indicator
    - Show last seen timestamp
    - Render appropriate controls based on device type
    - _Requirements: 1.2, 10.2_
  
  - [ ]* 8.2 Write property test for device card
    - **Property 1: Device card rendering completeness**
    - **Property 22: Device type icon mapping**
    - **Validates: Requirements 1.2, 10.2**
  
  - [x] 8.3 Create LightControl component
    - Implement components/devices/LightControl.tsx
    - Add toggle switch for on/off control
    - Add brightness slider for dimmable lights
    - Implement debounced brightness updates
    - Show loading state during command execution
    - _Requirements: 2.1, 2.3, 2.4, 8.1, 8.4_
  
  - [ ]* 8.4 Write property test for light control
    - **Property 3: Dimmable light slider presence**
    - **Property 17: Command loading state**
    - **Validates: Requirements 2.3, 8.1, 8.4**
  
  - [x] 8.5 Create SensorDisplay component
    - Implement components/devices/SensorDisplay.tsx
    - Display current sensor value with units
    - Show data freshness indicator
    - Handle multiple sensor readings
    - Format values with appropriate precision
    - _Requirements: 3.1, 3.3, 3.4, 3.5_
  
  - [ ]* 8.6 Write property test for sensor display
    - **Property 7: Multi-value sensor display**
    - **Property 8: Sensor value formatting**
    - **Property 9: Stale data indication**
    - **Validates: Requirements 3.3, 3.4, 3.5**
  
  - [x] 8.7 Create DeviceGrid component
    - Implement components/devices/DeviceGrid.tsx
    - Create responsive grid layout (desktop: multi-column, tablet: 2-column, mobile: 1-column)
    - Handle empty state with message
    - Show loading skeletons
    - _Requirements: 1.3, 7.1, 7.2, 7.3_

- [ ] 9. Implement chart components
  - [ ] 9.1 Install and configure Recharts
    - Install recharts library
    - Create chart theme configuration
    - _Requirements: 4.1_
  
  - [ ] 9.2 Create SensorChart component
    - Implement components/charts/SensorChart.tsx
    - Create line chart for historical data
    - Add time range selector (1h, 6h, 24h, 7d)
    - Implement responsive sizing
    - Add tooltip with formatted values
    - Handle loading and error states
    - _Requirements: 4.1, 4.2, 4.5_
  
  - [ ]* 9.3 Write property test for sensor chart
    - **Property 11: Sensor type filtering**
    - **Validates: Requirements 4.4**

- [x] 10. Implement Dashboard page
  - [x] 10.1 Create Dashboard page component
    - Implement pages/Dashboard.tsx
    - Integrate DeviceGrid component
    - Connect to device store
    - Start polling on mount, stop on unmount
    - Add device type/location filtering
    - _Requirements: 1.1, 1.2, 3.2_
  
  - [x] 10.2 Implement device command handling
    - Add command execution logic with optimistic updates
    - Handle command success and failure
    - Show notifications for command results
    - Revert state on failure
    - _Requirements: 2.2, 2.5, 8.2, 8.3_
  
  - [ ]* 10.3 Write property test for command handling
    - **Property 5: Command failure state reversion**
    - **Validates: Requirements 2.5**

- [ ] 11. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 12. Implement Logs page
  - [ ] 12.1 Create Logs page component
    - Implement pages/Logs.tsx
    - Create table layout for log entries
    - Display timestamp, log level, and message
    - Add log level filtering (info, warning, error)
    - _Requirements: 5.1, 5.2, 5.4_
  
  - [ ]* 12.2 Write property test for log display
    - **Property 12: Log entry display completeness**
    - **Property 13: Log level filtering**
    - **Validates: Requirements 5.2, 5.4**
  
  - [ ] 12.3 Implement pagination
    - Add pagination controls
    - Handle page changes
    - Update API calls with page parameters
    - _Requirements: 5.5_
  
  - [ ]* 12.4 Write property test for pagination
    - **Property 14: Pagination correctness**
    - **Validates: Requirements 5.5**
  
  - [ ] 12.5 Add auto-refresh capability
    - Implement polling for new log entries
    - Add manual refresh button
    - _Requirements: 5.3_

- [ ] 13. Implement utility functions
  - [ ] 13.1 Create formatters
    - Implement utils/formatters.ts
    - Add date/time formatting functions
    - Add number formatting with precision
    - Add unit conversion functions
    - _Requirements: 3.5, 5.2_
  
  - [ ] 13.2 Create constants
    - Implement utils/constants.ts
    - Define device type icons mapping
    - Define sensor type units
    - Define polling intervals
    - _Requirements: 10.2_

- [ ] 14. Implement error handling
  - [ ] 14.1 Add global error handling
    - Implement error interceptor in Axios
    - Add retry logic with exponential backoff
    - Handle network timeouts
    - _Requirements: 9.4, 9.5_
  
  - [ ]* 14.2 Write property test for error handling
    - **Property 21: Network error handling**
    - **Validates: Requirements 9.5**
  
  - [ ] 14.3 Add user-friendly error messages
    - Map HTTP status codes to messages
    - Display errors in notifications
    - Provide retry options
    - _Requirements: 1.5, 2.5_

- [ ] 15. Implement responsive design
  - [x] 15.1 Add responsive breakpoints
    - Configure MUI breakpoints in theme
    - Test layouts at different viewport sizes
    - _Requirements: 7.1, 7.2, 7.3_
  
  - [-] 15.2 Optimize for mobile
    - Ensure touch-friendly controls
    - Test mobile navigation menu
    - Verify single-column layout on mobile
    - _Requirements: 6.4, 7.3_

- [ ] 16. Setup environment configuration
  - [ ] 16.1 Create environment files
    - Create .env.development with local API URL
    - Create .env.production with production API URL
    - Add polling interval configuration
    - _Requirements: All_
  
  - [ ] 16.2 Configure Vite for environment variables
    - Update vite.config.ts to handle env vars
    - Add type definitions for import.meta.env
    - _Requirements: All_

- [ ] 17. Implement build and deployment
  - [ ] 17.1 Configure production build
    - Optimize Vite build configuration
    - Enable code splitting
    - Configure asset optimization
    - _Requirements: All_
  
  - [ ] 17.2 Setup static file serving in ApiGateway
    - Copy build output to ApiGateway wwwroot folder
    - Verify static file serving configuration
    - Test SPA fallback routing
    - _Requirements: All_
  
  - [ ] 17.3 Create build script
    - Create npm script to build and copy to ApiGateway
    - Add clean script to remove old builds
    - _Requirements: All_

- [ ] 18. Final testing and polish
  - [ ] 18.1 Test all user flows
    - Test viewing devices on Dashboard
    - Test controlling lights (toggle, brightness)
    - Test viewing sensor data and charts
    - Test viewing logs with filtering
    - Test navigation between pages
    - _Requirements: All_
  
  - [ ] 18.2 Test error scenarios
    - Test API unavailable
    - Test network timeout
    - Test invalid commands
    - Test empty states
    - _Requirements: 1.3, 1.5, 2.5, 4.5_
  
  - [ ] 18.3 Test responsive design
    - Test on desktop (1920x1080)
    - Test on tablet (768x1024)
    - Test on mobile (375x667)
    - _Requirements: 7.1, 7.2, 7.3_
  
  - [ ]* 18.4 Run all property-based tests
    - Execute all property tests with 100+ iterations
    - Verify all properties pass
    - Fix any failing properties
    - _Requirements: All_

- [ ] 19. Final Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 20. Documentation
  - [ ] 20.1 Create README.md
    - Document project setup instructions
    - Add development and build commands
    - Document environment variables
    - Add architecture overview
    - _Requirements: All_
  
  - [ ] 20.2 Add code comments
    - Document complex logic
    - Add JSDoc comments for public APIs
    - Document component props
    - _Requirements: All_
