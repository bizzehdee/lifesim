using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class GenomeBlendTests
{
    [Fact]
    public void Blend_everyContinuousTrait_isTheArithmeticMeanOfTheParents()
    {
        var a = new Genome
        {
            Size = 2.0,
            SpeedCapacity = 4.0,
            ThermalCenter = 10.0,
            ThermalWidth = 6.0,
            EnvRadius = 8.0,
            OrgRadius = 3.0,
            SensoryAcuity = 0.2,
            MetabolicEfficiency = 0.4,
            Armour = 0.6,
            Evasion = 0.8,
            Toxicity = 0.1,
            Plasticity = 0.3,
            LearningDecay = 0.5,
            Sexuality = 0.9,
            ShareFraction = 0.7,
            GermWeight = 1.0,
            FeederWeight = 2.0,
            StoreWeight = 3.0,
            DefenderWeight = 4.0,
            MoverWeight = 5.0,
            SensorWeight = 6.0,
        };
        var b = new Genome
        {
            Size = 4.0,
            SpeedCapacity = 8.0,
            ThermalCenter = 20.0,
            ThermalWidth = 2.0,
            EnvRadius = 4.0,
            OrgRadius = 1.0,
            SensoryAcuity = 0.4,
            MetabolicEfficiency = 0.2,
            Armour = 0.2,
            Evasion = 0.4,
            Toxicity = 0.3,
            Plasticity = 0.7,
            LearningDecay = 0.1,
            Sexuality = 0.1,
            ShareFraction = 0.3,
            GermWeight = 3.0,
            FeederWeight = 4.0,
            StoreWeight = 5.0,
            DefenderWeight = 6.0,
            MoverWeight = 7.0,
            SensorWeight = 8.0,
        };

        Genome child = Genome.Blend(a, b);

        Assert.Equal(3.0, child.Size);
        Assert.Equal(6.0, child.SpeedCapacity);
        Assert.Equal(15.0, child.ThermalCenter);
        Assert.Equal(4.0, child.ThermalWidth);
        Assert.Equal(6.0, child.EnvRadius);
        Assert.Equal(2.0, child.OrgRadius);
        Assert.Equal(0.3, child.SensoryAcuity, 12);
        Assert.Equal(0.3, child.MetabolicEfficiency, 12);
        Assert.Equal(0.4, child.Armour, 12);
        Assert.Equal(0.6, child.Evasion, 12);
        Assert.Equal(0.2, child.Toxicity, 12);
        Assert.Equal(0.5, child.Plasticity, 12);
        Assert.Equal(0.3, child.LearningDecay, 12);
        Assert.Equal(0.5, child.Sexuality, 12);
        Assert.Equal(0.5, child.ShareFraction, 12);
        Assert.Equal(2.0, child.GermWeight);
        Assert.Equal(3.0, child.FeederWeight);
        Assert.Equal(4.0, child.StoreWeight);
        Assert.Equal(5.0, child.DefenderWeight);
        Assert.Equal(6.0, child.MoverWeight);
        Assert.Equal(7.0, child.SensorWeight);
    }

    [Fact]
    public void Blend_cellCount_takesTheMaxSoAUnicellularMateDoesNotDiluteABodyPlan()
    {
        var multicellular = new Genome { CellCount = 6.0 };
        var unicellular = new Genome { CellCount = 1.0 };

        Assert.Equal(6.0, Genome.Blend(multicellular, unicellular).CellCount);
        Assert.Equal(6.0, Genome.Blend(unicellular, multicellular).CellCount);
    }

    [Fact]
    public void Blend_isCommutativeForTheMeanTraits()
    {
        var a = new Genome { Size = 1.0, Sexuality = 0.2, ShareFraction = 0.8 };
        var b = new Genome { Size = 3.0, Sexuality = 0.6, ShareFraction = 0.4 };

        Genome ab = Genome.Blend(a, b);
        Genome ba = Genome.Blend(b, a);

        Assert.Equal(ab.Size, ba.Size);
        Assert.Equal(ab.Sexuality, ba.Sexuality);
        Assert.Equal(ab.ShareFraction, ba.ShareFraction);
    }
}
