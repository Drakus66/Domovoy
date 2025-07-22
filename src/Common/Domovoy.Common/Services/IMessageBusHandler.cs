using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;

namespace Domovoy.Common.Services;

/// <summary>
/// Defines the contract for handling message bus operations in the Domovoy system.
/// This interface provides methods for publishing and subscribing to commands and events.
/// </summary>
public interface IMessageBusHandler
{
    /// <summary>
    /// Sets up message bus subscriptions for the service.
    /// This method should be called during service initialization.
    /// </summary>
    void SetupMessageBusSubscriptions();

    /// <summary>
    /// Publishes a command to the specified exchange with the given routing key.
    /// </summary>
    /// <typeparam name="T">The type of command to publish, must inherit from BaseCommand.</typeparam>
    /// <param name="exchange">The exchange to publish the command to.</param>
    /// <param name="command">The command to publish.</param>
    /// <param name="routingKey">The routing key for message routing.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishCommand<T>(string exchange, T command, string routingKey) where T : BaseCommand;

    /// <summary>
    /// Publishes an event to the specified exchange with the given routing key.
    /// </summary>
    /// <typeparam name="T">The type of event to publish, must inherit from BaseEvent.</typeparam>
    /// <param name="exchange">The exchange to publish the event to.</param>
    /// <param name="event">The event to publish.</param>
    /// <param name="routingKey">The routing key for message routing.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishEvent<T>(string exchange, T @event, string routingKey) where T : BaseEvent;

    /// <summary>
    /// Subscribes to commands of a specific type on the given queue.
    /// </summary>
    /// <typeparam name="T">The type of command to subscribe to, must inherit from BaseCommand.</typeparam>
    /// <param name="queueName">The name of the queue to subscribe to.</param>
    /// <param name="handler">The handler function to process received commands.</param>
    void SubscribeToCommands<T>(string queueName, Func<T, Task> handler) where T : BaseCommand;

    /// <summary>
    /// Subscribes to events of a specific type on the given queue.
    /// </summary>
    /// <typeparam name="T">The type of event to subscribe to, must inherit from BaseEvent.</typeparam>
    /// <param name="queueName">The name of the queue to subscribe to.</param>
    /// <param name="handler">The handler function to process received events.</param>
    void SubscribeToEvents<T>(string queueName, Func<T, Task> handler) where T : BaseEvent;
}