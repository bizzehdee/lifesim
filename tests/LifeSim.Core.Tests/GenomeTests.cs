using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class GenomeTests
{
    [Fact]
    public void Random_drawsEveryTraitWithinBounds_asAUnicellularFounder()
    {
        var bounds = new TraitBounds();
        var prng = new Prng(123);

        for (int i = 0; i < 50; i++)
        {
            Genome g = Genome.Random(bounds, prng);
            Assert.Equal(1.0, g.CellCount); // founders are unicellular
            Assert.InRange(g.Size, bounds.Size.Min, bounds.Size.Max);
            Assert.InRange(g.ThermalCenter, bounds.ThermalCenter.Min, bounds.ThermalCenter.Max);
            Assert.InRange(g.SensoryAcuity, bounds.SensoryAcuity.Min, bounds.SensoryAcuity.Max);
            Assert.InRange(g.ShareFraction, bounds.ShareFraction.Min, bounds.ShareFraction.Max);
        }
    }

    [Fact]
    public void Random_producesAVariedGenePool_notCloneCopies()
    {
        var bounds = new TraitBounds();
        var prng = new Prng(7);
        var sizes = new HashSet<double>();
        for (int i = 0; i < 20; i++)
        {
            sizes.Add(Genome.Random(bounds, prng).Size);
        }

        Assert.True(sizes.Count > 1, "Successive random founders should differ.");
    }

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
