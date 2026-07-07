using System.Text.Json;

using Domovoy.DbGateway.Endpoints;
using Domovoy.DbGateway.Models;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the custom-dashboards feature: input validation (<see cref="DashboardEndpoints.Normalize"/>)
/// and the BSON round-trip of a <see cref="DashboardDocument"/> whose item <c>Params</c> carry
/// <c>JsonElement</c> values — the shape they arrive in over HTTP through the gateway. Pure — no infrastructure.
/// </summary>
public sealed class DashboardTests
{
    // --- Normalize: structural validation without Mongo -------------------------------------------

    [Fact]
    public void Normalize_RejectsEmptyName()
    {
        var (doc, error) = DashboardEndpoints.Normalize(new DashboardEndpoints.DashboardInput("  ", null, null));

        Assert.Null(doc);
        Assert.Contains("name", error);
    }

    [Fact]
    public void Normalize_RejectsUnknownItemType()
    {
        var input = new DashboardEndpoints.DashboardInput("Climate", null, new List<DashboardSection>
        {
            new() { Title = "Main", Items = { new DashboardItem { Type = "gauge", DeviceId = "d1" } } },
        });

        var (doc, error) = DashboardEndpoints.Normalize(input);

        Assert.Null(doc);
        Assert.Contains("gauge", error);
    }

    [Theory]
    [InlineData("device")]
    [InlineData("capability")]
    [InlineData("chart")]
    public void Normalize_RequiresDeviceIdForDeviceBoundTypes(string type)
    {
        var input = new DashboardEndpoints.DashboardInput("Tab", null, new List<DashboardSection>
        {
            new() { Items = { new DashboardItem { Type = type, CapabilityId = "temperature" } } },
        });

        var (doc, error) = DashboardEndpoints.Normalize(input);

        Assert.Null(doc);
        Assert.Contains("deviceId", error);
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("chart")]
    public void Normalize_RequiresCapabilityIdForCapabilityBoundTypes(string type)
    {
        var input = new DashboardEndpoints.DashboardInput("Tab", null, new List<DashboardSection>
        {
            new() { Items = { new DashboardItem { Type = type, DeviceId = "d1" } } },
        });

        var (doc, error) = DashboardEndpoints.Normalize(input);

        Assert.Null(doc);
        Assert.Contains("capabilityId", error);
    }

    [Fact]
    public void Normalize_AcceptsValidInput_TrimsAndPreservesOrder()
    {
        var input = new DashboardEndpoints.DashboardInput("  Climate  ", "  thermostat  ", new List<DashboardSection>
        {
            new()
            {
                Title = " Bedroom ",
                Items =
                {
                    new DashboardItem { Type = "modes" },
                    new DashboardItem { Type = "device", DeviceId = "d1" },
                    new DashboardItem { Type = "capability", DeviceId = "d2", CapabilityId = "humidity" },
                    new DashboardItem { Type = "chart", DeviceId = "d2", CapabilityId = "temperature" },
                },
            },
            new() { Title = "Hall", Items = { new DashboardItem { Type = "device", DeviceId = "d3" } } },
        });

        var (doc, error) = DashboardEndpoints.Normalize(input);

        Assert.Null(error);
        Assert.NotNull(doc);
        Assert.Equal("Climate", doc!.Name);
        Assert.Equal("thermostat", doc.Icon);
        Assert.Equal(new[] { "Bedroom", "Hall" }, doc.Sections.Select(s => s.Title));
        Assert.Equal(new[] { "modes", "device", "capability", "chart" },
            doc.Sections[0].Items.Select(i => i.Type));
    }

    [Fact]
    public void Normalize_ModesItemNeedsNoDevice()
    {
        var input = new DashboardEndpoints.DashboardInput("Pult", null, new List<DashboardSection>
        {
            new() { Items = { new DashboardItem { Type = "modes" } } },
        });

        var (doc, error) = DashboardEndpoints.Normalize(input);

        Assert.Null(error);
        Assert.NotNull(doc);
    }

    // --- BSON round-trip: the one genuinely risky persistence detail ------------------------------

    [Fact]
    public void DashboardDocument_WithJsonElementParams_RoundTripsThroughBson()
    {
        BsonTestSerializers.EnsureRegistered();

        // Params arrive as JsonElement when the gateway relays the WebUI's JSON body
        // (System.Text.Json boxes object values as JsonElement).
        var chartParams = JsonSerializer.Deserialize<Dictionary<string, object>>(
            """{"hours": 24, "bucket": "hour"}""")!;

        var doc = new DashboardDocument
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Climate",
            Icon = "thermostat",
            Order = 3,
            Sections = new List<DashboardSection>
            {
                new()
                {
                    Title = "Bedroom",
                    Items =
                    {
                        new DashboardItem { Type = "chart", DeviceId = "d1", CapabilityId = "temperature", Params = chartParams },
                        new DashboardItem { Type = "modes" },
                    },
                },
            },
        };

        var bson = doc.ToBsonDocument();
        var restored = BsonSerializer.Deserialize<DashboardDocument>(bson);

        Assert.Equal(doc.Id, restored.Id);
        Assert.Equal("Climate", restored.Name);
        Assert.Equal(3, restored.Order);
        var section = Assert.Single(restored.Sections);
        Assert.Equal("Bedroom", section.Title);
        Assert.Equal(2, section.Items.Count);

        var chart = section.Items[0];
        Assert.Equal("chart", chart.Type);
        Assert.NotNull(chart.Params);
        // JsonElement 24 → BSON Int32 → int; "hour" → BSON String → string (JsonObjectDictionarySerializer).
        Assert.Equal(24, Assert.IsType<int>(chart.Params!["hours"]));
        Assert.Equal("hour", Assert.IsType<string>(chart.Params["bucket"]));

        Assert.Null(section.Items[1].Params);
    }

    [Fact]
    public void DashboardPrefs_RoundTripsThroughBson()
    {
        BsonTestSerializers.EnsureRegistered();

        var prefs = new DashboardPrefs { HiddenSpheres = { "switch", "other" } };

        var restored = BsonSerializer.Deserialize<DashboardPrefs>(prefs.ToBsonDocument());

        Assert.Equal(DashboardPrefs.SingletonId, restored.Id);
        Assert.Equal(new[] { "switch", "other" }, restored.HiddenSpheres);
    }
}
