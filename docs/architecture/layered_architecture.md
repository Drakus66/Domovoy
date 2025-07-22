# Domovoy Application Layered Architecture

## Overview

Domovoy follows a modern microservices architecture with clear separation of concerns through layered design within each service. The application is built on .NET technology stack with Docker containerization and comprehensive monitoring.

## Architecture Layers

### 1. Presentation Layer
- **API Gateway** (`Domovoy.ApiGateway`): Serves as the entry point for client applications, handling routing, request aggregation, and cross-cutting concerns
- **UI Components**: Web-based dashboard for device management and monitoring

### 2. Service Layer
- **Device Service** (`Domovoy.DeviceService`): Manages device registration, state, and commands
- **Light Service** (`Domovoy.LightService`): Specialized service for lighting control
- **Sensor Service** (`Domovoy.SensorService`): Manages various types of sensors
- **Device Discovery Service**: Handles MQTT device discovery and integration

### 3. Domain Layer
- **Domain Models**: Core business entities (Devices, Sensors, Lights, etc.)
- **Domain Services**: Encapsulation of business logic
- **Domain Events**: Event-based communication for domain changes

### 4. Infrastructure Layer
- **Message Bus** (`Domovoy.MessageBus`): Event-driven communication between services using RabbitMQ
- **Database Gateway** (`Domovoy.DbGateway`): Data access and persistence
- **Common Utilities** (`Domovoy.Common`): Shared code and utilities

### 5. Cross-Cutting Concerns
- **Authentication & Authorization**: User and device security
- **Logging & Monitoring**: Structured logging with Loki, metrics with Prometheus
- **Configuration**: Centralized configuration management

## Inter-Layer Communication

- **Service-to-Service Communication**: Primarily through the Message Bus using events
- **Client-to-Service Communication**: Through the API Gateway using REST APIs
- **Device-to-Service Communication**: Through MQTT protocol for IoT devices

## Dependency Flow

The dependency flow follows the Dependency Inversion Principle:

1. Presentation Layer → Service Layer → Domain Layer
2. Infrastructure Layer → Domain Layer
3. Cross-cutting concerns are injected where needed

This ensures that the domain layer remains isolated and free from external dependencies, while the service layer coordinates the application workflow.
