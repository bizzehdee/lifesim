using LifeSim.Core.Configuration;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class GenomeTests
{
    [Fact]
    public void MidRange_sitsAtTheMidpointOfEveryBound()
    {
        var bounds = new TraitBounds();
        Genome genome = Genome.MidRange(bounds);

        Assert.Equal((bounds.Size.Min + bounds.Size.Max) / 2.0, genome.Size);
        Assert.Equal((bounds.SensoryAcuity.Min + bounds.SensoryAcuity.Max) / 2.0, genome.SensoryAcuity);
    }

    [Fact]
    public void Clamped_clampsEveryTraitToItsBounds()
    {
        var bounds = new TraitBounds();
        var wild = new Genome
        {
            Size = bounds.Size.Max + 100,
            SpeedCapacity = bounds.SpeedCapacity.Min - 100,
            ThermalCenter = bounds.ThermalCenter.Max + 100,
            ThermalWidth = bounds.ThermalWidth.Min - 100,
            EnvRadius = bounds.EnvRadius.Max + 100,
            OrgRadius = bounds.OrgRadius.Min - 100,
            SensoryAcuity = bounds.SensoryAcuity.Max + 100,
        };

        Genome clamped = wild.Clamped(bounds);

        Assert.Equal(bounds.Size.Max, clamped.Size);
        Assert.Equal(bounds.SpeedCapacity.Min, clamped.SpeedCapacity);
        Assert.Equal(bounds.ThermalCenter.Max, clamped.ThermalCenter);
        Assert.Equal(bounds.ThermalWidth.Min, clamped.ThermalWidth);
        Assert.Equal(bounds.EnvRadius.Max, clamped.EnvRadius);
        Assert.Equal(bounds.OrgRadius.Min, clamped.OrgRadius);
        Assert.Equal(bounds.SensoryAcuity.Max, clamped.SensoryAcuity);
    }

    [Fact]
    public void Clamped_leavesInBoundsValuesUnchanged()
    {
        var bounds = new TraitBounds();
        Genome inBounds = Genome.MidRange(bounds);

        Assert.Equal(inBounds, inBounds.Clamped(bounds));
    }
}
