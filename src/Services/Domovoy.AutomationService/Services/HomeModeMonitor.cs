using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Keeps <see cref="HomeModeState"/> live by subscribing to <see cref="HomeModeChangedV1"/> on the bus
/// (roadmap Epic 1G). The DbGateway is the persistence authority and the publisher; this just mirrors the
/// latest mode into the engine so rule <c>Mode</c> conditions and the <see cref="PresenceMonitor"/> see it
/// without an HTTP round-trip. The startup value is seeded separately by <see cref="RefreshLoop"/>.
/// </summary>
public sealed class HomeModeMonitor : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly HomeModeState _mode;
    private readonly ILogger<HomeModeMonitor> _logger;

    public HomeModeMonitor(IMessageBus bus, HomeModeState mode, ILogger<HomeModeMonitor> logger)
    {
        _bus = bus;
        _mode = mode;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _bus.SubscribeAsync<Envelope<HomeModeChangedV1>>(
            "automation-home-mode",
            BusTopology.EventsExchange,
            BusTopology.HomeModeChangedKey,
            env =>
            {
                var change = env.Data;
                if (change is not null)
                {
                    _mode.Set(change.Mode);
                    _logger.LogInformation("Home mode is now {Mode} (by {Source})", change.Mode, change.Source);
                }
                return Task.CompletedTask;
            },
            stoppingToken);

        _logger.LogInformation("HomeModeMonitor subscribed to home-mode changes");
    }
}
