using LifeSim.Core.Neat;
using LifeSim.Core.Sensing;

namespace LifeSim.App.Presentation;

/// <summary>
/// Human-readable names for the brain's sensory inputs, for the inspector's brain-graph tooltips. An
/// input node's id is its <see cref="SensoryField"/> value (input ids are 0..<see cref="NeatTopology.InputCount"/>-1),
/// so a node can be named straight from its id.
/// </summary>
public static class SensoryFieldLabels
{
    private static readonly IReadOnlyDictionary<SensoryField, string> Labels = new Dictionary<SensoryField, string>
    {
        [SensoryField.Energy] = "Energy",
        [SensoryField.Age] = "Age",
        [SensoryField.TileTemperature] = "Tile temperature",
        [SensoryField.BiomeFriction] = "Biome friction",
        [SensoryField.RichestTileDistance] = "Richest food · distance",
        [SensoryField.RichestTileDirectionX] = "Richest food · direction X",
        [SensoryField.RichestTileDirectionY] = "Richest food · direction Y",
        [SensoryField.ClosestOrganismDistance] = "Nearest organism · distance",
        [SensoryField.ClosestOrganismDirectionX] = "Nearest organism · direction X",
        [SensoryField.ClosestOrganismDirectionY] = "Nearest organism · direction Y",
        [SensoryField.ClosestOrganismSizeDelta] = "Nearest organism · size delta",
        [SensoryField.NearbySmallerCount] = "Nearby smaller count",
        [SensoryField.NearbyLargerCount] = "Nearby larger count",
        [SensoryField.LocalDensity] = "Local density",
        [SensoryField.LastActionResult] = "Last action result",
        [SensoryField.ReproductiveReadiness] = "Reproductive readiness",
        [SensoryField.GlobalStressLevel] = "Global stress level",
        [SensoryField.ClosestOrganismRelatedness] = "Nearest organism · relatedness",
        [SensoryField.ClosestOrganismToxicity] = "Nearest organism · toxicity",
        [SensoryField.LightLevel] = "Light level",
        [SensoryField.DayPhaseSin] = "Day phase (sin)",
        [SensoryField.DayPhaseCos] = "Day phase (cos)",
        [SensoryField.SeasonPhaseSin] = "Season phase (sin)",
        [SensoryField.SeasonPhaseCos] = "Season phase (cos)",
        [SensoryField.LightDirectionX] = "Brightest tile · direction X",
        [SensoryField.LightDirectionY] = "Brightest tile · direction Y",
    };

    /// <summary>A readable label for a sensory field (falls back to the enum name).</summary>
    public static string Describe(SensoryField field) =>
        Labels.TryGetValue(field, out string? label) ? label : field.ToString();

    /// <summary>
    /// The sensory field a brain input node carries, if <paramref name="nodeId"/> is an input node id.
    /// Returns false for hidden/output nodes.
    /// </summary>
    public static bool TryForInputNode(long nodeId, out SensoryField field)
    {
        if (nodeId >= 0 && nodeId < NeatTopology.InputCount)
        {
            field = (SensoryField)(int)nodeId;
            return true;
        }

        field = default;
        return false;
    }
}
