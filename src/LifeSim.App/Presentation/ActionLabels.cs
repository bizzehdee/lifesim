using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;

namespace LifeSim.App.Presentation;

/// <summary>
/// Human-readable names for the brain's action outputs, for the inspector's brain-graph tooltips. An
/// output node's id is <see cref="NeatTopology.InputCount"/> + its <see cref="OrganismAction"/> value
/// (output ids are InputCount..InputCount+<see cref="NeatTopology.OutputCount"/>-1), so a node can be
/// named straight from its id.
/// </summary>
public static class ActionLabels
{
    private static readonly IReadOnlyDictionary<OrganismAction, string> Labels = new Dictionary<OrganismAction, string>
    {
        [OrganismAction.MoveNorth] = "Move north",
        [OrganismAction.MoveSouth] = "Move south",
        [OrganismAction.MoveEast] = "Move east",
        [OrganismAction.MoveWest] = "Move west",
        [OrganismAction.HarvestSelf] = "Harvest here",
        [OrganismAction.HarvestNorth] = "Harvest north",
        [OrganismAction.HarvestSouth] = "Harvest south",
        [OrganismAction.HarvestEast] = "Harvest east",
        [OrganismAction.HarvestWest] = "Harvest west",
        [OrganismAction.Idle] = "Idle",
        [OrganismAction.Reproduce] = "Reproduce",
        [OrganismAction.ShareNorth] = "Share north",
        [OrganismAction.ShareSouth] = "Share south",
        [OrganismAction.ShareEast] = "Share east",
        [OrganismAction.ShareWest] = "Share west",
    };

    /// <summary>A readable label for an action (falls back to the enum name).</summary>
    public static string Describe(OrganismAction action) =>
        Labels.TryGetValue(action, out string? label) ? label : action.ToString();

    /// <summary>
    /// The action a brain output node drives, if <paramref name="nodeId"/> is an output node id.
    /// Returns false for input/hidden nodes.
    /// </summary>
    public static bool TryForOutputNode(long nodeId, out OrganismAction action)
    {
        long index = nodeId - NeatTopology.InputCount;
        if (index >= 0 && index < NeatTopology.OutputCount)
        {
            action = (OrganismAction)(int)index;
            return true;
        }

        action = default;
        return false;
    }
}
