using System.Text.Json;
using System.Text.Json.Serialization;

using Domovoy.Common.Models;
using Domovoy.Common.Models.Events;
using Domovoy.Common.Models.Commands;
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
    /// <summary>
    /// Provides base functionality for device services in the Domovoy system.
    /// This abstract class implements common device operations and message bus handling
    /// following the Gateway Pattern architecture where DB access is centralized through DbGateway.
    /// </summary>
    public abstract class BaseService : BackgroundService, IDeviceService, IMessageBusHandler
    {
        protected readonly IMessageBus MessageBus;
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;
        protected readonly BaseServiceOptions Options;

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
        }

        /// <summary>
        /// Starts the service and sets up message bus subscriptions
        /// </summary>
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation("{ServiceName} starting", GetType().Name);
            SetupMessageBusSubscriptions();
            SetupOrchestrationSubscription();
            return Task.CompletedTask;
        }

        private void SetupOrchestrationSubscription()
        {
            // Subscribe to orchestration commands targeting this service
            MessageBus.SubscribeAsync<OrchestrationCommand>(
                $"domovoy.orchestration.{GetType().Name}",
                "domovoy.commands",
                "command.orchestration.*",
                HandleOrchestrationCommand);

            Logger.LogInformation("Subscribed to orchestration commands");
        }

        private async Task HandleOrchestrationCommand(OrchestrationCommand cmd)
        {
            // Filter: only handle commands for this service or "all"
            if (!string.Equals(cmd.ServiceName, GetType().Name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(cmd.ServiceName, "all", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Logger.LogInformation("Received orchestration command: {Action} for {ServiceName}", cmd.Action, cmd.ServiceName);

            if (cmd.Action == OrchestrationAction.Restart)
            {
                Logger.LogWarning("Service restart requested via orchestration. Exiting process...");

                // Allow some time for logs to flush
                await Task.Delay(1000);

                // Exit process to trigger Docker restart
                Environment.Exit(1);
            }
        }

        /// <inheritdoc/>
        public abstract Task HandleDeviceCommand(BaseCommand command);

        /// <inheritdoc/>
        public abstract Task HandleDeviceEvent(BaseEvent @event);

        /// <inheritdoc/>
        public abstract void SetupMessageBusSubscriptions();

        /// <inheritdoc/>
        public abstract Task UpdateDeviceState(string deviceId, Dictionary<string, object> state);

        /// <inheritdoc/>
        public abstract Task UpdateDeviceOnlineStatus(string deviceId, bool isOnline);

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
                    var currentValue = timestampProperty!.GetValue(@event) as DateTime?;
                    if (currentValue == null || currentValue == default(DateTime))
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
            MessageBus.SubscribeAsync<T>(queueName, Options.CommandExchange, "command.*", handler);
            Logger.LogInformation("Subscribed to commands of type {CommandType} on queue {QueueName}", typeof(T).Name, queueName);
        }

        /// <inheritdoc/>
        public virtual void SubscribeToEvents<T>(string queueName, Func<T, Task> handler) where T : BaseEvent
        {
            MessageBus.SubscribeAsync<T>(queueName, Options.EventExchange, "event.*", handler);
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
    }
}
