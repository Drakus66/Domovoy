namespace Domovoy.Common.Services
{
    /// <summary>
    /// Configuration options for BaseService
    /// </summary>
    public class BaseServiceOptions
    {
        /// <summary>
        /// The base URL for the DB Gateway service
        /// </summary>
        public string DbGatewayBaseUrl { get; set; } = "http://domovoy-dbgateway";
        
        /// <summary>
        /// The interval at which to save device states (in seconds)
        /// </summary>
        public int StateUpdateIntervalSeconds { get; set; } = 30;
        
        /// <summary>
        /// The default message exchange for device commands
        /// </summary>
        public string CommandExchange { get; set; } = "device.commands";
        
        /// <summary>
        /// The default message exchange for device events
        /// </summary>
        public string EventExchange { get; set; } = "device.events";
    }
}
