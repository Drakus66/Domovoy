using System.Text.Json.Serialization;

namespace Domovoy.Common.Models
{
    /// <summary>
    /// Base class for all entities in the Domovoy system.
    /// Provides common properties and functionality for database entities.
    /// </summary>
    public abstract class BaseEntity
    {
        /// <summary>
        /// Gets the unique identifier for the entity.
        /// </summary>
        [JsonPropertyName("id")] public readonly Guid Id = Guid.NewGuid();

        public Guid LocationId { get; set; }

        /// <summary>
        /// Gets or sets the name of the entity.
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Gets or sets the date and time the entity was last updated.
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Gets or sets the metadata for the entity.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();

        /// <summary>
        /// Gets a value indicating whether the entity is online.
        /// </summary>
        [JsonIgnore]
        public bool IsOnline => (DateTime.UtcNow - LastUpdated).TotalMinutes < 5;
    }
}
