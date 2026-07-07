using LifeSim.Core.Configuration;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class KinshipTests
{
    private static readonly TraitBounds Bounds = new();

    [Fact]
    public void Relatedness_ofIdenticalGenomes_isOne()
    {
        Genome g = Genome.MidRange(Bounds);
        Assert.Equal(1.0, Kinship.Relatedness(g, g, Bounds), precision: 9);
    }

    [Fact]
    public void Relatedness_isLowerForDivergentGenomes_andSymmetric()
    {
        Genome a = Genome.MidRange(Bounds);
        Genome b = a with { Size = Bounds.Size.Max, SpeedCapacity = Bounds.SpeedCapacity.Max };

        double ab = Kinship.Relatedness(a, b, Bounds);
        Assert.True(ab < 1.0);
        Assert.True(ab >= 0.0);
        Assert.Equal(ab, Kinship.Relatedness(b, a, Bounds), precision: 9); // symmetric
    }

    [Fact]
    public void Relatedness_ofMaximallyDivergentGenomes_isZero()
    {
        Genome low = new()
        {
            Size = Bounds.Size.Min,
            SpeedCapacity = Bounds.SpeedCapacity.Min,
            ThermalCenter = Bounds.ThermalCenter.Min,
            ThermalWidth = Bounds.ThermalWidth.Min,
            EnvRadius = Bounds.EnvRadius.Min,
            OrgRadius = Bounds.OrgRadius.Min,
            SensoryAcuity = Bounds.SensoryAcuity.Min,
        };
        Genome high = new()
        {
            Size = Bounds.Size.Max,
            SpeedCapacity = Bounds.SpeedCapacity.Max,
            ThermalCenter = Bounds.ThermalCenter.Max,
            ThermalWidth = Bounds.ThermalWidth.Max,
            EnvRadius = Bounds.EnvRadius.Max,
            OrgRadius = Bounds.OrgRadius.Max,
            SensoryAcuity = Bounds.SensoryAcuity.Max,
        };

        Assert.Equal(0.0, Kinship.Relatedness(low, high, Bounds), precision: 9);
    }
}
