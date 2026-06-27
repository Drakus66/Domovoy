using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Ml;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// Registry of built-in (E1) block types (roadmap Epic 1H). New first-party types register here; new
/// <i>instances</i> are pure config. The catalog also drives the UI's typed authoring form (the schema
/// of ports/params/outputs is served via <c>GET /api/blocks/catalog</c>).
/// </summary>
public sealed class BlockCatalog
{
    private readonly Dictionary<string, IBlockType> _types;

    public BlockCatalog(MlModelService models, IOptions<AutomationOptions> options)
    {
        var o = options.Value;
        var types = new IBlockType[]
        {
            new EwmaFilterType(),
            new ThermostatType(),
            new Co2VentilationType(),
            new IrrigationSequencerType(),
            new MlSetpointType(models, o.SetpointMin, o.SetpointMax), // Epic 2A: ML-driven setpoint
            new MlThermostatType(models, o.SetpointMin, o.SetpointMax), // Epic 2B: staged ML setpoint governor
        };
        _types = types.ToDictionary(t => t.TypeId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IBlockType> Types => _types.Values;

    public IBlockType? Get(string typeId) =>
        typeId is not null && _types.TryGetValue(typeId, out var t) ? t : null;
}
