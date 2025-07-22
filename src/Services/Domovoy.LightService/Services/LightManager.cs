using System.Text.Json;

using Domovoy.Common.Models.Devices;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;
using Domovoy.Common.Models.Enums;
using Domovoy.Common.Services;
using Domovoy.MessageBus;

using Microsoft.Extensions.Options;

namespace Domovoy.LightService.Services
{
    /// <summary>
    /// Manages light devices in the Domovoy system.
    /// This service handles light-specific commands, events, and state management
    /// following the Gateway Pattern architecture where DB access is centralized through DbGateway.
    /// Also provides support for MQTT-based light devices.
    /// </summary>
    public class LightManager : BaseService
    {
        private readonly Dictionary<string, Light> _lights = new();
        private readonly Dictionary<string, DateTime> _lightLastModified = new();

        /// <summary>
        /// Initializes a new instance of the LightManager class.
        /// </summary>
        /// <param name="messageBus">The message bus for pubsub operations</param>
        /// <param name="httpClientFactory">Factory for creating HTTP clients</param>
        /// <param name="logger">Logger for the service</param>
        /// <param name="options">Service configuration options</param>
        public LightManager(
            IMessageBus messageBus,
            IHttpClientFactory httpClientFactory,
            ILogger<LightManager> logger,
            IOptions<BaseServiceOptions> options)
            : base(messageBus, httpClientFactory, logger, options)
        {
        }

        public override Task HandleDeviceCommand(BaseCommand command)
        {
            throw new NotImplementedException();
        }

        public override Task HandleDeviceEvent(BaseEvent @event)
        {
            throw new NotImplementedException();
        }

        public override void SetupMessageBusSubscriptions()
        {
            throw new NotImplementedException();
        }
    }
}
