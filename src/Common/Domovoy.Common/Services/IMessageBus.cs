namespace Domovoy.MessageBus;

public interface IMessageBus : IDisposable
{
    Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default);
    Task SubscribeAsync<T>(string queue, string exchange, string routingKey, Func<T, Task> handler, CancellationToken cancellationToken = default);
    Task UnsubscribeAsync(string queue, CancellationToken cancellationToken = default);
}
