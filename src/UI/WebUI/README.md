# Domovoy WebUI

Modern web interface for the Domovoy smart home system built with React, TypeScript, and Vite.

## Features

- Real-time device monitoring and control
- Sensor data visualization with historical charts
- System logs viewer
- Responsive design for desktop, tablet, and mobile
- Material-UI component library

## Tech Stack

- **Framework**: React 18+ with TypeScript
- **Build Tool**: Vite
- **State Management**: Zustand
- **HTTP Client**: Axios
- **Routing**: React Router v6
- **UI Components**: Material-UI (MUI) v5
- **Charts**: Recharts
- **Testing**: Vitest + React Testing Library + fast-check

## Project Structure

```
src/
├── api/              # API client and service functions
├── components/       # React components
│   ├── common/      # Reusable components
│   ├── devices/     # Device-related components
│   ├── charts/      # Chart components
│   └── layout/      # Layout components
├── hooks/           # Custom React hooks
├── pages/           # Page components
├── store/           # Zustand stores
├── types/           # TypeScript type definitions
├── utils/           # Utility functions
└── test/            # Test setup and utilities
```

## Getting Started

### Prerequisites

- Node.js 18+ 
- npm or yarn

### Installation

```bash
# Install dependencies
npm install
```

### Development

```bash
# Start development server
npm run dev
```

The application will be available at `http://localhost:3000`

### Build

```bash
# Build for production
npm run build
```

The build output will be in the `dist/` directory.

### Testing

```bash
# Run tests once
npm test

# Run tests in watch mode
npm run test:watch

# Run tests with UI
npm run test:ui
```

## Environment Variables

Create `.env.development` and `.env.production` files:

```env
VITE_API_BASE_URL=http://localhost:5000
VITE_POLL_INTERVAL=1000
VITE_APP_VERSION=1.0.0
```

## API Integration

The WebUI communicates with the Domovoy ApiGateway via REST API. The API base URL is configured through environment variables.

### Polling Strategy

- Automatic data refresh every 1 second (configurable)
- Batched API requests to reduce server load
- Pauses polling when browser tab is hidden
- Exponential backoff on API failures

## Development Guidelines

### TypeScript

- Strict mode enabled
- All code must be properly typed
- Avoid `any` types when possible

### Component Structure

- Use functional components with hooks
- Keep components small and focused
- Extract reusable logic into custom hooks
- Use Material-UI components for consistency

### State Management

- Use Zustand for global state
- Keep local state in components when appropriate
- Implement optimistic updates for better UX

### Testing

- Write unit tests for components and utilities
- Use property-based testing for critical logic
- Mock API calls with MSW
- Aim for high test coverage

## License

Private - Domovoy Smart Home System
