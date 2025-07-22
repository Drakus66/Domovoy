using System.Text.Json;
using System.Text.Json.Serialization;

using Domovoy.Common.Models;
using Domovoy.Common.Models.Events;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Enums;
using Domovoy.MessageBus;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Domovoy.Common.Services
{

    /// <summary>
    /// Provides base functionality for device services in the Domovoy system.
    /// This abstract class implements common device operations and message bus handling
    /// following the Gateway Pattern architecture where DB access is centralized through DbGateway.
    /// </summary>
    public abstract class BaseService : BackgroundService, IDeviceService, IMessageBusHandler, IAsyncDisposable
    {
        protected readonly IMessageBus MessageBus;
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;
        protected readonly Timer StateUpdateTimer;
        protected readonly BaseServiceOptions Options;
        
        // Dictionary to track device states between updates
        protected readonly Dictionary<string, Dictionary<string, object>> DeviceStates = new();
        protected readonly Dictionary<string, bool> DeviceOnlineStatuses = new();

        /// <summary>
        /// Initializes a new instance of the BaseService class.
        /// </summary>
        /// <param name="messageBus">The RabbitMQ connection for message bus operations.</param>
        /// <param name="httpClientFactory">The HTTP client factory for making HTTP requests.</param>
        /// <param name="logger">The logger for service logging.</param>
        /// <param name="options">The configuration options for the service.</param>
        /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
        protected BaseService(
            IMessageBus messageBus,
            IHttpClientFactory httpClientFactory,
            ILogger logger,
            IOptions<BaseServiceOptions> options)
        {
            MessageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
            HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Options = options?.Value ?? throw new ArgumentNullException(nameof(options));

            var updateInterval = TimeSpan.FromSeconds(Options.StateUpdateIntervalSeconds);
            StateUpdateTimer = new Timer(SaveStates, null, TimeSpan.Zero, updateInterval);
        }

        /// <summary>
        /// Starts the service and sets up message bus subscriptions
        /// </summary>
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation("{ServiceName} starting", GetType().Name);
            SetupMessageBusSubscriptions();
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public abstract Task HandleDeviceCommand(BaseCommand command);

        /// <inheritdoc/>
        public abstract Task HandleDeviceEvent(BaseEvent @event);

        /// <summary>
        /// Updates the state of a device and publishes a state update event to the message bus
        /// </summary>
        /// <param name="deviceId">The ID of the device to update</param>
        /// <param name="state">The new state of the device</param>
        public virtual async Task UpdateDeviceState(string deviceId, Dictionary<string, object> state)
        {
            if (string.IsNullOrEmpty(deviceId))
                throw new ArgumentNullException(nameof(deviceId));
                
            if (state == null)
                throw new ArgumentNullException(nameof(state));
                
            try
            {
                // Store state locally for batched updates
                DeviceStates[deviceId] = state;
                
                // Publish state update event
                var stateEvent = new DeviceStateUpdatedEvent
                {
                    DeviceId = deviceId,
                    State = state,
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = Guid.NewGuid(), // Добавляем обязательное свойство
                    Success = true // Устанавливаем Success по умолчанию
                };
                
                // Добавляем источник события в Data вместо использования несуществующего свойства Source
                stateEvent.Data["Source"] = GetType().Name;
                
                await PublishEvent(
                    Options.EventExchange,
                    stateEvent,
                    "event.device.state.updated"
                );
                
                Logger.LogDebug("Updated state for device {DeviceId}", deviceId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error updating state for device {DeviceId}", deviceId);
                throw;
            }
        }

        /// <summary>
        /// Updates the online status of a device and publishes a status update event to the message bus
        /// </summary>
        /// <param name="deviceId">The ID of the device to update</param>
        /// <param name="isOnline">Whether the device is online</param>
        public virtual async Task UpdateDeviceOnlineStatus(string deviceId, bool isOnline)
        {
            if (string.IsNullOrEmpty(deviceId))
                throw new ArgumentNullException(nameof(deviceId));
                
            try
            {
                // Store status locally for batched updates
                DeviceOnlineStatuses[deviceId] = isOnline;
                
                // Publish status update event
                var statusEvent = new DeviceStatusChangedEvent
                {
                    DeviceId = deviceId,
                    IsOnline = isOnline,
                    Timestamp = DateTime.UtcNow,
                    Source = GetType().Name
                };
                
                await PublishEvent(
                    Options.EventExchange,
                    statusEvent,
                    "event.device.status.changed"
                );
                
                Logger.LogDebug("Updated online status for device {DeviceId} to {IsOnline}", deviceId, isOnline);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error updating online status for device {DeviceId}", deviceId);
                throw;
            }
        }

        /// <inheritdoc/>
        public abstract void SetupMessageBusSubscriptions();

        /// <inheritdoc/>
        public virtual async Task PublishCommand<T>(string exchange, T command, string routingKey) where T : BaseCommand
        {
            try
            {
                // Set command metadata if not already set
                if (command.Timestamp == default)
                {
                    command.Timestamp = DateTime.UtcNow;
                }
                if (string.IsNullOrEmpty(command.Source))
                {
                    command.Source = GetType().Name;
                }
                
                await MessageBus.PublishAsync(exchange, routingKey, command);
                Logger.LogInformation("Published command {CommandType} to {Exchange} with routing key {RoutingKey}",
                    typeof(T).Name, exchange, routingKey);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error publishing command {CommandType} to {Exchange}", typeof(T).Name, exchange);
                throw;
            }
        }

        /// <inheritdoc/>
        public virtual async Task PublishEvent<T>(string exchange, T @event, string routingKey) where T : BaseEvent
        {
            try
            {
                // Set event metadata if not already set
                // Timestamp может быть не у всех типов, проверяем через рефлексию
                var timestampProperty = typeof(T).GetProperty("Timestamp");
                if (timestampProperty != null && timestampProperty.PropertyType == typeof(DateTime))
                {
                    var currentValue = (DateTime)timestampProperty.GetValue(@event);
                    if (currentValue == default)
                    {
                        timestampProperty.SetValue(@event, DateTime.UtcNow);
                    }
                }
                // Source может быть не у всех типов, проверяем через рефлексию
                var sourceProperty = typeof(T).GetProperty("Source");
                if (sourceProperty != null)
                {
                    var sourceValue = sourceProperty.GetValue(@event);
                    var currentValue = sourceValue as string;
                    if (string.IsNullOrEmpty(currentValue))
                    {
                        sourceProperty.SetValue(@event, GetType().Name);
                    }
                }
                
                await MessageBus.PublishAsync(exchange, routingKey, @event);
                Logger.LogInformation("Published event {EventType} to {Exchange} with routing key {RoutingKey}",
                    typeof(T).Name, exchange, routingKey);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error publishing event {EventType} to {Exchange}", typeof(T).Name, exchange);
                throw;
            }
        }

        /// <inheritdoc/>
        public virtual void SubscribeToCommands<T>(string queueName, Func<T, Task> handler) where T : BaseCommand
        {
            MessageBus.SubscribeAsync(queueName, Options.CommandExchange, "command.*", handler);
            Logger.LogInformation("Subscribed to commands of type {CommandType} on queue {QueueName}", 
typeof(T).Name, queueName);
        }

        /// <inheritdoc/>
        public virtual void SubscribeToEvents<T>(string queueName, Func<T, Task> handler) where T : BaseEvent
        {
            MessageBus.SubscribeAsync(queueName, Options.EventExchange, "event.*", handler);
            Logger.LogInformation("Subscribed to events of type {EventType} on queue {QueueName}", typeof(T).Name, queueName);
        }

        /// <summary>
        /// Loads an entity from the database through the DbGateway.
        /// </summary>
        /// <typeparam name="T">The type of entity to load.</typeparam>
        /// <param name="id">The unique identifier of the entity.</param>
        /// <param name="endpoint">The API endpoint to load the entity from.</param>
        /// <returns>The loaded entity, or null if not found.</returns>
        protected virtual async Task<T?> LoadFromDb<T>(string id, string endpoint) where T : BaseEntity
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentNullException(nameof(id));
                
            if (string.IsNullOrEmpty(endpoint))
                throw new ArgumentNullException(nameof(endpoint));
                
            try
            {
                var httpClient = HttpClientFactory.CreateClient("DbGateway");
                var response = await httpClient.GetAsync($"/api/{endpoint}/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogWarning("Failed to load {Type} with ID {Id} from database. Status code: {StatusCode}",
                        typeof(T).Name, id, response.StatusCode);
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading {Type} with ID {Id} from database", typeof(T).Name, id);
                throw;
            }
        }

        /// <summary>
        /// Saves an entity to the database through the DbGateway.
        /// </summary>
        /// <typeparam name="T">The type of entity to save.</typeparam>
        /// <param name="entity">The entity to save.</param>
        /// <param name="endpoint">The API endpoint to save the entity to.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        protected virtual async Task SaveToDb<T>(T entity, string endpoint) where T : BaseEntity
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
                
            if (string.IsNullOrEmpty(endpoint))
                throw new ArgumentNullException(nameof(endpoint));
                
            try
            {
                var httpClient = HttpClientFactory.CreateClient("DbGateway");
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var content = JsonSerializer.Serialize(entity, options);
                var response = await httpClient.PostAsync(
                    $"/api/{endpoint}",
                    new StringContent(content, System.Text.Encoding.UTF8, "application/json")
                );

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError("Failed to save {Type} with ID {Id} to database. Status code: {StatusCode}",
                        typeof(T).Name, entity.Id, response.StatusCode);
                    throw new HttpRequestException($"Failed to save {typeof(T).Name} to database");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving {Type} with ID {Id} to database", typeof(T).Name, entity.Id);
                throw;
            }
        }

        /// <summary>
        /// Saves the current state of all managed devices to the database through the DbGateway.
        /// This method is called periodically by the state update timer to batch updates and reduce load.
        /// </summary>
        /// <param name="state">State object passed by the Timer.</param>
        protected virtual async void SaveStates(object? state)
        {
            try
            {
                // Process device states that need to be saved
                var statesToSave = new Dictionary<string, Dictionary<string, object>>(DeviceStates);
                DeviceStates.Clear();
                
                foreach (var (deviceId, deviceState) in statesToSave)
                {
                    var stateUpdate = new DeviceStateUpdate
                    {
                        DeviceId = deviceId,
                        State = deviceState,
                        Timestamp = DateTime.UtcNow,
                        Name = $"State_{deviceId}_{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}"
                    };
                    
                    await SaveToDb(stateUpdate, "devices/state");
                }
                
                // Process device online statuses that need to be saved
                var statusesToSave = new Dictionary<string, bool>(DeviceOnlineStatuses);
                DeviceOnlineStatuses.Clear();
                
                foreach (var (deviceId, isOnline) in statusesToSave)
                {
                    var statusUpdate = new DeviceStatusUpdate
                    {
                        DeviceId = deviceId,
                        IsOnline = isOnline,
                        Timestamp = DateTime.UtcNow,
                        Name = $"Status_{deviceId}_{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}"
                    };
                    
                    await SaveToDb(statusUpdate, "devices/status");
                }
                
                if (statesToSave.Count > 0 || statusesToSave.Count > 0)
                {
                    Logger.LogInformation("Saved {StateCount} device states and {StatusCount} device statuses",
                        statesToSave.Count, statusesToSave.Count);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving device states and statuses");
            }
        }

        /// <summary>
        /// Configures the HttpClientFactory with the DbGateway base URL.
        /// This should be called in the service registration.
        /// </summary>
        public static void ConfigureHttpClient(IServiceCollection services, IConfiguration configuration)
        {
            var options = configuration.GetSection("BaseService").Get<BaseServiceOptions>() ?? new BaseServiceOptions();
            
            services.AddHttpClient("DbGateway", client =>
            {
                client.BaseAddress = new Uri(options.DbGatewayBaseUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });
        }

        /// <summary>
        /// Asynchronously releases resources used by the service.
        /// </summary>
        protected virtual async ValueTask DisposeAsyncCore()
        {
            // Save any pending states before shutting down
            await Task.Run(() => SaveStates(null));
            await StateUpdateTimer.DisposeAsync();
            
            // Unsubscribe from message bus
            // This would depend on how MessageBus interface is implemented
            // MessageBus.Dispose();
        }

        /// <summary>
        /// Asynchronously releases resources used by the service.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();
            GC.SuppressFinalize(this);
        }
    }
}
