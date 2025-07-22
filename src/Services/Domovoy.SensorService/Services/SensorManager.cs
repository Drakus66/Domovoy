using System.Text.Json;

using Domovoy.Common.Models.Devices;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Enums;
using Domovoy.Common.Models.Enums.EntityTypes;
using Domovoy.Common.Models.Events;
using Domovoy.Common.Services;
using Domovoy.MessageBus;

using Microsoft.Extensions.Options;

namespace Domovoy.SensorService.Services
{
    /// <summary>
    /// Manages sensor devices in the Domovoy system.
    /// This service handles sensor-specific commands, events, and state management
    /// following the Gateway Pattern architecture where DB access is centralized through DbGateway.
    /// Also provides support for MQTT-based sensor devices.
    /// </summary>
    public class SensorManager : BaseService
    {
        private readonly Dictionary<string, Sensor> _sensors = new();
        private readonly Dictionary<string, DateTime> _sensorLastModified = new();
        private readonly Dictionary<string, Timer> _reportingTimers = new();
        private readonly Dictionary<string, SensorReportingConfig> _reportingConfigs = new();

        /// <summary>
        /// Initializes a new instance of the SensorManager class.
        /// </summary>
        /// <param name="messageBus">The message bus for pubsub operations</param>
        /// <param name="httpClientFactory">Factory for creating HTTP clients</param>
        /// <param name="logger">Logger for the service</param>
        /// <param name="options">Service configuration options</param>
        public SensorManager(
            IMessageBus messageBus,
            IHttpClientFactory httpClientFactory,
            ILogger<SensorManager> logger,
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
