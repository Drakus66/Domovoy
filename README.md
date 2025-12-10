# Domovoy

**Smart Home Automation Platform with IoT Device Management**

Domovoy is a comprehensive .NET-based home automation platform featuring microservices architecture, MQTT device integration, and real-time monitoring. Built for scalability and flexibility, it supports various IoT devices through Arduino gateways and provides a robust foundation for smart home automation.

## 🚀 Features

### Core Capabilities
- **Device Management**: Complete lifecycle management for IoT devices
- **Light Control**: Advanced lighting control (on/off, brightness, color, temperature)
- **Sensor Integration**: Multi-sensor support (temperature, humidity, motion, light quality)
- **MQTT Discovery**: Automatic device discovery using Home Assistant format
- **Real-time Communication**: SignalR for instant updates
- **Monitoring**: Comprehensive observability with Prometheus, Grafana, and Loki

### Architecture Highlights
- **Microservices**: 6 independent services with clear boundaries
- **Event-Driven**: Asynchronous communication via RabbitMQ
- **API Gateway**: Unified entry point with Ocelot
- **Database Gateway**: Centralized MongoDB data access
- **Containerized**: Full Docker Compose deployment

## 📋 System Status

**Current Version**: Development (December 2025)
- ✅ Core Services: 80% Complete
- 🚧 MQTT Integration: 70% Complete
- 📋 Security Layer: 20% Complete
- 🔧 Arduino Gateway: 5% Complete

## 🏗️ Architecture

```
Client Layer (Web/Mobile/MQTT)
         ↓
    API Gateway (Ocelot)
         ↓
Service Layer (Device/Light/Sensor/Discovery)
         ↓
    Message Bus (RabbitMQ AMQP+MQTT)
         ↓
    DB Gateway (MongoDB)
```

## 🛠️ Technology Stack

- **.NET 6+** - Core framework
- **ASP.NET Core** - Web APIs
- **RabbitMQ** - Message broker (AMQP + MQTT)
- **MongoDB** - Document database
- **Docker** - Containerization
- **Ocelot** - API Gateway
- **SignalR** - Real-time communication
- **Prometheus/Grafana/Loki** - Monitoring stack
- **Arduino** - Hardware gateways (ESP8266/ESP32)

## 🚦 Getting Started

### Prerequisites
- Docker & Docker Compose
- .NET 6+ SDK (for development)
- MongoDB
- RabbitMQ with MQTT plugin

### Quick Start
```bash
# Clone the repository
git clone <repository-url>
cd Domovoy

# Start all services
docker-compose up -d

# Check service health
docker-compose ps
```

### Service Endpoints
- **API Gateway**: http://localhost:5000
- **Grafana**: http://localhost:3000
- **Prometheus**: http://localhost:9090
- **RabbitMQ Management**: http://localhost:15672

## 📁 Project Structure

```
Domovoy/
├── src/
│   ├── Common/              # Shared libraries
│   │   ├── Domovoy.Common/
│   │   └── Domovoy.MessageBus/
│   ├── Gateway/             # Gateway services
│   │   ├── Domovoy.ApiGateway/
│   │   └── Domovoy.DbGateway/
│   └── Services/            # Business services
│       ├── Domovoy.DeviceService/
│       ├── Domovoy.LightService/
│       ├── Domovoy.SensorService/
│       └── Domovoy.DiscoveryService/
├── Arduino/                 # Arduino gateway firmware
├── docs/                    # Documentation
├── memory-bank/            # Project context & state
├── docker-compose.yml      # Service orchestration
└── README.md
```

## 📚 Documentation

- **Architecture**: [docs/architecture/](docs/architecture/)
- **Current State**: [memory-bank/currentState.md](memory-bank/currentState.md)
- **System Overview**: [memory-bank/systemOverview.md](memory-bank/systemOverview.md)
- **Task Plans**: [tasks_plan.txt](tasks_plan.txt)
- **Arduino Tasks**: [arduino_tasks_plan.txt](arduino_tasks_plan.txt)

## 🔧 Development

### Building Services
```bash
# Build all services
dotnet build Domovoy.sln

# Run specific service
cd src/Services/Domovoy.DeviceService
dotnet run
```

### Running Tests
```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true
```

## 🎯 Roadmap

### Phase 1: MQTT Core (Current - Q1 2026)
- Device heartbeat mechanism
- MQTT command adapter
- Telemetry handlers
- Basic authentication

### Phase 2: Security (Q1 2026)
- TLS/SSL configuration
- Device authentication
- Message encryption
- Authorization rules

### Phase 3: Arduino Gateway (Q2 2026)
- Base MQTT client
- WiFi management
- Sensor/control templates
- Remote configuration

### Phase 4: Production Ready (Q2 2026)
- Comprehensive testing
- Performance optimization
- Deployment automation
- Complete documentation

## 🤝 Contributing

Contributions are welcome! Please read our contributing guidelines before submitting PRs.

## 📄 License

[Specify your license here]

## 📞 Contact

[Your contact information]

---

**Status**: Active Development | **Last Updated**: December 3, 2025
