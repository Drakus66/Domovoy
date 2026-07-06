using Domovoy.AutomationService.Ml;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;

using Xunit;

using Ex = Domovoy.AutomationService.Ml.ArchetypeClassifier.Example;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the ML.NET device-archetype classifier (roadmap Epic 2D). Verifies it learns the
/// capability-signature → archetype mapping and generalizes to an unseen signature. Pure — no infrastructure.
/// </summary>
public sealed class ArchetypeClassifierTests
{
    private static ArchetypeClassifier Trained()
    {
        var examples = new List<Ex>();
        // A few near-duplicate signatures per archetype so SDCA has separable classes to learn.
        for (var i = 0; i < 4; i++)
        {
            examples.Add(new Ex(new[] { CapabilityIds.OnOff, CapabilityIds.Brightness, CapabilityIds.ColorTemp },
                new[] { CapabilityIds.OnOff, CapabilityIds.Brightness }, DeviceArchetypes.Light));
            examples.Add(new Ex(new[] { CapabilityIds.Temperature, CapabilityIds.TemperatureSetpoint },
                new[] { CapabilityIds.TemperatureSetpoint }, DeviceArchetypes.Thermostat));
            examples.Add(new Ex(new[] { CapabilityIds.Occupancy, CapabilityIds.Battery },
                Array.Empty<string>(), DeviceArchetypes.Motion));
            examples.Add(new Ex(new[] { CapabilityIds.Lock, CapabilityIds.Battery },
                new[] { CapabilityIds.Lock }, DeviceArchetypes.Lock));
        }

        var clf = new ArchetypeClassifier();
        var n = clf.Train(examples);
        Assert.Equal(examples.Count, n);
        return clf;
    }

    [Fact]
    public void Predicts_TrainedSignatures()
    {
        var clf = Trained();

        Assert.Equal(DeviceArchetypes.Light,
            clf.Predict(new[] { CapabilityIds.OnOff, CapabilityIds.Brightness, CapabilityIds.ColorTemp },
                new[] { CapabilityIds.OnOff, CapabilityIds.Brightness })!.Archetype);

        Assert.Equal(DeviceArchetypes.Thermostat,
            clf.Predict(new[] { CapabilityIds.Temperature, CapabilityIds.TemperatureSetpoint },
                new[] { CapabilityIds.TemperatureSetpoint })!.Archetype);

        Assert.Equal(DeviceArchetypes.Lock,
            clf.Predict(new[] { CapabilityIds.Lock, CapabilityIds.Battery }, new[] { CapabilityIds.Lock })!.Archetype);
    }

    [Fact]
    public void Generalizes_ToUnseenLightSignature()
    {
        var clf = Trained();
        // A light variant never trained on (brightness + color instead of color_temp) still classifies as light.
        var p = clf.Predict(new[] { CapabilityIds.OnOff, CapabilityIds.Brightness, CapabilityIds.Color },
            new[] { CapabilityIds.OnOff, CapabilityIds.Brightness });

        Assert.NotNull(p);
        Assert.Equal(DeviceArchetypes.Light, p!.Archetype);
        Assert.True(p.Confidence > 0.4f, $"low confidence {p.Confidence}");
    }

    [Fact]
    public void Untrained_ReturnsNull()
    {
        var clf = new ArchetypeClassifier();
        Assert.False(clf.IsTrained);
        Assert.Null(clf.Predict(new[] { CapabilityIds.OnOff }, new[] { CapabilityIds.OnOff }));
    }

    [Fact]
    public void TooFewClasses_DoesNotTrain()
    {
        var clf = new ArchetypeClassifier();
        var oneClass = Enumerable.Range(0, 6)
            .Select(_ => new Ex(new[] { CapabilityIds.OnOff }, new[] { CapabilityIds.OnOff }, DeviceArchetypes.Switch))
            .ToList();
        Assert.Equal(0, clf.Train(oneClass));
        Assert.False(clf.IsTrained);
    }
}
