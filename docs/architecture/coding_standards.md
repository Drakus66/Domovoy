# Domovoy Coding Standards and Patterns

## Overview

This document defines the coding standards, design patterns, and best practices that should be followed when developing the Domovoy project. Consistent adherence to these standards ensures code quality, maintainability, and scalability of the codebase.

## Code Organization

### Project Structure

- **Solution Structure**: Follow the established microservices architecture with clear boundaries
- **Namespaces**: Use `Domovoy.[ServiceName].[Layer]`
- **Assembly References**: Services should only reference Common libraries and never reference each other directly

### File Organization

- One class per file (except for small related classes)
- Files should be named after the primary class they contain
- Group related files in appropriate folders
- Maximum file length should be 1000 lines (aim for 300-500)

## C# Coding Style

### Naming Conventions

- **Classes, Methods, Properties**: PascalCase (e.g., `DeviceManager`)
- **Private fields**: camelCase with underscore prefix (e.g., `_deviceRepository`)
- **Local variables and parameters**: camelCase (e.g., `deviceId`)
- **Constants**: PascalCase (e.g., `MaxRetryAttempts`)
- **Interfaces**: Prefix with "I" (e.g., `IDeviceRepository`)
- **Enums**: Singular name in PascalCase, members in PascalCase

### Code Formatting

- Use 4 spaces for indentation (not tabs)
- Use `var` when the type is obvious
- Use explicit type when it improves readability
- Always use braces for control structures (if, for, while, etc.)
- Opening braces on the same line, closing braces on a new line
- One statement per line
- Limit line length to 120 characters

### Code Comments

- Use XML documentation comments for public APIs
- Add summary comments for classes and public methods
- Document parameters and return values
- Avoid obvious comments that don't add value
- Comment complex algorithms and business rules

```csharp
/// <summary>
/// Manages device operations including registration and state updates
/// </summary>
public class DeviceManager : IDeviceManager
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IMessageBus _messageBus;
    
    /// <summary>
    /// Initializes a new instance of the DeviceManager
    /// </summary>
    /// <param name="deviceRepository">Repository for device persistence</param>
    /// <param name="messageBus">Message bus for event publication</param>
    public DeviceManager(IDeviceRepository deviceRepository, IMessageBus messageBus)
    {
        _deviceRepository = deviceRepository;
        _messageBus = messageBus;
    }
    
    // Methods follow...
}
```

## Design Patterns and Principles

### Core Principles

1. **SOLID Principles**
   - Single Responsibility Principle
   - Open/Closed Principle
   - Liskov Substitution Principle
   - Interface Segregation Principle
   - Dependency Inversion Principle

2. **DRY (Don't Repeat Yourself)**
   - Extract common functionality into reusable components
   - Use shared libraries for cross-cutting concerns

3. **YAGNI (You Aren't Gonna Need It)**
   - Only implement features when they are needed
   - Avoid speculative generality

### Architectural Patterns

1. **Dependency Injection**
   - Use constructor injection for required dependencies
   - Register services in the DI container appropriately (transient, scoped, singleton)
   - Avoid service locator pattern

2. **Repository Pattern**
   - Use repositories to abstract data access
   - Repositories should handle entity persistence and retrieval
   - Return domain entities, not data transfer objects

3. **Mediator Pattern**
   - Use for complex service-to-service communication
   - Implement using MediatR library for request/response patterns

4. **CQRS (Command Query Responsibility Segregation)**
   - Separate command (write) and query (read) models where appropriate
   - Use commands for operations that change state
   - Use queries for operations that read state

5. **Domain-Driven Design**
   - Model the domain accurately with entities, value objects, and aggregates
   - Define bounded contexts aligned with microservices
   - Use domain events for cross-service communication

### Implementation Patterns

1. **Async/Await**
   - Use asynchronous programming for I/O-bound operations
   - Follow the Task-based Asynchronous Pattern (TAP)
   - Avoid mixing async and sync code paths

2. **Options Pattern**
   - Use IOptions<T> for configuration
   - Define typed configuration classes
   - Validate configuration at startup

3. **Factory Pattern**
   - Use for creating complex objects
   - Isolate construction logic from business logic

4. **Decorator Pattern**
   - Use for adding cross-cutting concerns (logging, caching, validation)
   - Register decorators in the DI container

## Error Handling and Logging

### Exception Handling

- Use exceptions for exceptional conditions, not control flow
- Create custom exception types for domain-specific errors
- Catch exceptions at service boundaries
- Never catch exceptions without proper handling
- Use try/catch blocks with specific exception types

### Logging

- Use structured logging with appropriate log levels
- Include context information in log entries
- Log at service boundaries and important operations
- Use correlation IDs for tracing requests across services
- Be cautious with sensitive information in logs

```csharp
public async Task<Device> RegisterDeviceAsync(DeviceRegistration registration)
{
    _logger.LogInformation("Registering device {DeviceId} of type {DeviceType}", 
                          registration.Id, registration.Type);
    
    try
    {
        var device = new Device
        {
            Id = registration.Id,
            Type = registration.Type,
            // Other properties
        };
        
        await _deviceRepository.AddAsync(device);
        
        await _messageBus.PublishAsync(new DeviceRegisteredEvent
        {
            DeviceId = device.Id,
            Timestamp = DateTime.UtcNow
        });
        
        return device;
    }
    catch (DuplicateDeviceException ex)
    {
        _logger.LogWarning(ex, "Attempt to register duplicate device {DeviceId}", registration.Id);
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to register device {DeviceId}", registration.Id);
        throw new DeviceRegistrationException($"Failed to register device: {registration.Id}", ex);
    }
}
```

## Testing Standards

### Unit Testing

- Use xUnit for unit tests
- Follow AAA pattern (Arrange, Act, Assert)
- Mock external dependencies using Moq
- Test one concept per test method
- Name tests clearly: [MethodUnderTest]_[Scenario]_[ExpectedResult]

### Integration Testing

- Test service interactions with external systems
- Use test containers for database and message bus dependencies
- Reset the state between test runs
- Avoid tests with dependencies on production systems

### Test Coverage

- Aim for 80% code coverage for business logic
- Focus on critical paths and complex logic
- Generate coverage reports as part of the CI pipeline

## Performance and Security Guidelines

### Performance

- Use async/await for I/O-bound operations
- Implement caching for frequently accessed, rarely changing data
- Optimize database queries (proper indexing, avoid N+1 queries)
- Use pagination for large result sets
- Benchmark critical operations

### Security

- Validate all inputs, especially from external sources
- Use parameterized queries to prevent SQL injection
- Never store secrets in code or config files (use secret management)
- Implement proper authentication and authorization
- Apply the principle of least privilege
- Use HTTPS for all communications
- Sanitize data before logging or displaying

## Dependencies and Licensing

Domovoy ships under **AGPL-3.0-or-later** with a **commercial dual-license** option. Every
third-party dependency must be compatible with **both** paths, so before adding any new NuGet or npm
package (or bumping one to a major that changes its license), **check the license first and pick only
a compatible one.**

### Allowed (permissive — no approval needed)

- **MIT, BSD-2-Clause, BSD-3-Clause, ISC, Apache-2.0, Zlib.**
- **MPL-2.0** and other file-level weak copyleft are acceptable, but when a package is dual-licensed
  (e.g. `RabbitMQ.Client` is Apache-2.0 / MPL-2.0), **elect the most permissive option** and note it.

### Disallowed without explicit owner approval

- **Strong copyleft: GPL-2.0/3.0, LGPL, AGPL.** A copyleft dependency would force the whole product
  open and **block the commercial license** — this is a hard stop, not a preference.
- **Source-available / non-OSI: SSPL, BSL, Elastic License, Commons Clause.**
- **"Ethical" / use-restricted licenses: the Hippocratic License and similar.** These add
  field-of-use restrictions that clash with the AGPL and with a clean commercial offer. (This is why
  `react-leaflet` was dropped in favour of `pigeon-maps`.)
- **No-license / "all rights reserved"** packages.

### Hygiene

- Avoid packages that squat an official namespace (e.g. a non-Microsoft package published under a
  `Microsoft.*` id) and avoid preview/unmaintained packages in production paths.
- Preserve upstream attribution: MIT/BSD/Apache require keeping copyright notices, so permissive
  dependencies must be reflected in a `THIRD-PARTY-NOTICES` file.
- When in doubt about a license, resolve it from the package's own repository (the `LICENSE` file),
  not from an auto-detector, and record the decision.

## Code Review Guidelines

### What to Look For

- License compatibility of any newly added dependency (see Dependencies and Licensing)

- Correctness: Does the code do what it's supposed to do?
- Adherence to coding standards and patterns
- Potential bugs and edge cases
- Security vulnerabilities
- Performance issues
- Sufficient test coverage

### Process

- All code must be reviewed before merging
- Provide constructive feedback
- Focus on the code, not the developer
- Approve only when all issues are addressed
- Use automated tools to catch common issues

## Versioning and Documentation

### Versioning

- Follow Semantic Versioning (MAJOR.MINOR.PATCH)
- Document breaking changes
- Use GitFlow for branch management

### Documentation

- Keep README files up to date
- Document APIs with XML comments
- Maintain architecture diagrams
- Document complex business rules and algorithms
- Create clear commit messages

## Continuous Integration/Continuous Deployment

- Run automated tests for all PRs
- Enforce code style and quality checks
- Run security scans
- Automate deployment to test environments
- Use feature flags for safe releases
