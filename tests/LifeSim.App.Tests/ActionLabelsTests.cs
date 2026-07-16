using LifeSim.App.Presentation;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;

namespace LifeSim.App.Tests;

public class ActionLabelsTests
{
    [Fact]
    public void EveryAction_hasAReadableLabel()
    {
        foreach (OrganismAction action in Enum.GetValues<OrganismAction>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ActionLabels.Describe(action)));
        }

        Assert.Equal("Move north", ActionLabels.Describe(OrganismAction.MoveNorth));
        Assert.Equal("Harvest here", ActionLabels.Describe(OrganismAction.HarvestSelf));
    }

    [Fact]
    public void TryForOutputNode_mapsOutputNodeIdsToTheirAction()
    {
        // Output node ids are InputCount + the OrganismAction value, 0 .. OutputCount-1.
        for (int i = 0; i < NeatTopology.OutputCount; i++)
        {
            Assert.True(ActionLabels.TryForOutputNode(NeatTopology.InputCount + i, out OrganismAction action));
            Assert.Equal((OrganismAction)i, action);
        }
    }

    [Fact]
    public void TryForOutputNode_rejectsNonOutputNodeIds()
    {
        Assert.False(ActionLabels.TryForOutputNode(NeatTopology.InputCount - 1, out _)); // last input id
        Assert.False(ActionLabels.TryForOutputNode(NeatTopology.InputCount + NeatTopology.OutputCount, out _));
    }
}
