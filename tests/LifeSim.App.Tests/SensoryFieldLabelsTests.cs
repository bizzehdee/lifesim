using LifeSim.App.Presentation;
using LifeSim.Core.Neat;
using LifeSim.Core.Sensing;

namespace LifeSim.App.Tests;

public class SensoryFieldLabelsTests
{
    [Fact]
    public void EverySensoryField_hasAReadableLabel()
    {
        foreach (SensoryField field in Enum.GetValues<SensoryField>())
        {
            Assert.False(string.IsNullOrWhiteSpace(SensoryFieldLabels.Describe(field)));
        }

        // The multi-word fields get a curated label (a spaced form), not the raw PascalCase enum name.
        Assert.Equal("Light level", SensoryFieldLabels.Describe(SensoryField.LightLevel));
        Assert.Equal("Tile temperature", SensoryFieldLabels.Describe(SensoryField.TileTemperature));
    }

    [Fact]
    public void TryForInputNode_mapsInputNodeIdsToTheirSensoryField()
    {
        // Input node ids are exactly the SensoryField values, 0 .. InputCount-1.
        for (int id = 0; id < NeatTopology.InputCount; id++)
        {
            Assert.True(SensoryFieldLabels.TryForInputNode(id, out SensoryField field));
            Assert.Equal((SensoryField)id, field);
        }
    }

    [Fact]
    public void TryForInputNode_rejectsNonInputNodeIds()
    {
        Assert.False(SensoryFieldLabels.TryForInputNode(-1, out _));
        Assert.False(SensoryFieldLabels.TryForInputNode(NeatTopology.InputCount, out _)); // first output id
        Assert.False(SensoryFieldLabels.TryForInputNode(NeatTopology.InputCount + NeatTopology.OutputCount, out _));
    }
}
