namespace Domovoy.Common.Models.Commands
{
    /// <summary>
    /// Base class for all commands in the Domovoy system.
    /// Provides common properties and functionality for command messages.
    /// </summary>
    public abstract class BaseCommand
    {
        /// <summary>
        /// Gets or sets the correlation ID for the command.
        /// </summary>
        public Guid CorrelationId { get; set; } = Guid.NewGuid();

        public Guid DeviceId { get; set; }

        /// <summary>
        /// Gets or sets additional parameters for the command.
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new();

        public DateTime Timestamp { get; set; }

        public string Source { get; set; } = "Unknown";
    }

}
