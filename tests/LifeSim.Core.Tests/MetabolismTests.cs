using LifeSim.Core.Configuration;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class MetabolismTests
{
    private static readonly MetabolismConfig Metabolic = new();
    private static readonly MovementCombatConfig MovementCombat = new();

    private static Genome NewGenome(double thermalCenter = 20.0, double thermalWidth = 10.0) => new()
    {
        Size = 2.0,
        SpeedCapacity = 1.0,
        ThermalCenter = thermalCenter,
        ThermalWidth = thermalWidth,
        EnvRadius = 3.0,
        OrgRadius = 2.0,
        SensoryAcuity = 0.5,
    };

    [Fact]
    public void EfficiencyCostMultiplier_dropsWithFrugality_butNeverToZero()
    {
        // Baseline efficiency pays full cost; maximal frugality shaves at most MaxMetabolicReduction,
        // so the multiplier stays strictly positive — cost asymptotes toward, never to, zero.
        Assert.Equal(1.0, Metabolism.EfficiencyCostMultiplier(NewGenome() with { MetabolicEfficiency = 0.0 }, Metabolic), precision: 10);
        Assert.Equal(1.0 - Metabolic.MaxMetabolicReduction, Metabolism.EfficiencyCostMultiplier(NewGenome() with { MetabolicEfficiency = 1.0 }, Metabolic), precision: 10);
        Assert.True(Metabolism.EfficiencyCostMultiplier(NewGenome() with { MetabolicEfficiency = 1.0 }, Metabolic) > 0.0);
    }

    [Fact]
    public void DefenseTax_chargesUpkeepOnTheSummedDefensiveTraits()
    {
        Genome undefended = NewGenome() with { Armour = 0, Evasion = 0, Toxicity = 0 };
        Genome defended = NewGenome() with { Armour = 0.5, Evasion = 0.25, Toxicity = 1.0 };

        Assert.Equal(0.0, Metabolism.DefenseTax(undefended, Metabolic), precision: 10);
        Assert.Equal((0.5 + 0.25 + 1.0) * Metabolic.DefenseUpkeep, Metabolism.DefenseTax(defended, Metabolic), precision: 10);
    }

    [Fact]
    public void EfficiencyYieldMultiplier_isTheRateYieldTradeOff()
    {
        // The price of frugality: less usable energy per graze, down to 1 - EfficiencyIntakePenalty.
        Assert.Equal(1.0, Metabolism.EfficiencyYieldMultiplier(NewGenome() with { MetabolicEfficiency = 0.0 }, Metabolic), precision: 10);
        Assert.Equal(1.0 - Metabolic.EfficiencyIntakePenalty, Metabolism.EfficiencyYieldMultiplier(NewGenome() with { MetabolicEfficiency = 1.0 }, Metabolic), precision: 10);
    }

    [Fact]
    public void CrowdingTax_isZeroWithinTheFreeAllowance_thenScalesPerNeighbour()
    {
        // Defaults: free 1 neighbour, 0.5 per additional.
        Assert.Equal(0.0, Metabolism.CrowdingTax(0, Metabolic));
        Assert.Equal(0.0, Metabolism.CrowdingTax(1, Metabolic));         // a kin pair is free
        Assert.Equal(0.5, Metabolism.CrowdingTax(2, Metabolic), precision: 10);
        Assert.Equal(3.5, Metabolism.CrowdingTax(8, Metabolic), precision: 10); // fully surrounded → (8-1)*0.5
    }

    [Fact]
    public void SenescenceTax_isZeroBeforeOnset_thenScalesWithAge()
    {
        // Defaults: onset 400 ticks, 0.02 per tick beyond it.
        Assert.Equal(0.0, Metabolism.SenescenceTax(0, Metabolic));
        Assert.Equal(0.0, Metabolism.SenescenceTax(400, Metabolic));       // exactly at onset — still free
        Assert.Equal(0.02, Metabolism.SenescenceTax(401, Metabolic), precision: 10);
        Assert.Equal(2.0, Metabolism.SenescenceTax(500, Metabolic), precision: 10); // (500-400)*0.02
    }

    [Fact]
    public void BaseMetabolism_isSizeTimesConfiguredBase()
    {
        Genome genome = NewGenome();
        Assert.Equal(genome.Size * Metabolic.BaseMetabolismPerSize, Metabolism.BaseMetabolism(genome, Metabolic));
    }

    [Fact]
    public void ThermalStress_isZero_insideTheEnvelope()
    {
        // Center 20, width 10 -> comfortable in [15, 25].
        Genome genome = NewGenome(thermalCenter: 20.0, thermalWidth: 10.0);

        Assert.Equal(0.0, Metabolism.ThermalStress(genome, 20.0, Metabolic));
        Assert.Equal(0.0, Metabolism.ThermalStress(genome, 15.0, Metabolic));
        Assert.Equal(0.0, Metabolism.ThermalStress(genome, 25.0, Metabolic));
    }

    [Fact]
    public void ThermalStress_scalesLinearlyBeyondTheEnvelope()
    {
        Genome genome = NewGenome(thermalCenter: 20.0, thermalWidth: 10.0);

        // 30 is 5 degrees past the [15, 25] envelope.
        double expected = 5.0 * Metabolic.ThermalStressScale;
        Assert.Equal(expected, Metabolism.ThermalStress(genome, 30.0, Metabolic), precision: 10);
    }

    [Fact]
    public void SensoryTax_matchesTheConfiguredFormula()
    {
        Genome genome = NewGenome();
        double expected = (genome.EnvRadius * Metabolic.SensoryTaxC1)
            + (genome.OrgRadius * genome.OrgRadius * Metabolic.SensoryTaxC2)
            + (genome.SensoryAcuity * Metabolic.SensoryTaxC3);

        Assert.Equal(expected, Metabolism.SensoryTax(genome, Metabolic), precision: 10);
    }

    [Fact]
    public void Total_sumsBaseThermalAndSensory()
    {
        Genome genome = NewGenome(thermalCenter: 20.0, thermalWidth: 10.0);
        double expected = Metabolism.BaseMetabolism(genome, Metabolic)
            + Metabolism.ThermalStress(genome, 40.0, Metabolic)
            + Metabolism.SensoryTax(genome, Metabolic);

        Assert.Equal(expected, Metabolism.Total(genome, 40.0, Metabolic), precision: 10);
    }

    [Fact]
    public void LocomotionTax_isDistanceTimesVelocitySquaredTimesFriction()
    {
        double tax = Metabolism.LocomotionTax(distance: 3.0, velocity: 2.0, biomeFriction: 1.5, MovementCombat);
        double expected = 3.0 * Math.Pow(2.0, MovementCombat.LocomotionVelocityExponent) * 1.5;

        Assert.Equal(expected, tax, precision: 10);
    }

    [Fact]
    public void CalculateCost_namesAndSumsTheSameCompleteEconomyChargedByTheEngine()
    {
        SimulationConfig config = SimulationConfig.Default;
        Genome genome = NewGenome() with
        {
            MetabolicEfficiency = 0.4,
            Armour = 0.3,
            Plasticity = 0.2,
            CellCount = 2.0,
        };

        MetabolicCostBreakdown result = Metabolism.CalculateCost(
            genome,
            age: config.Metabolism.SenescenceOnsetAge + 2,
            tileTemperature: 40,
            distanceTraveled: 1,
            biomeFriction: 1.5,
            localDensity: config.Events.PlagueDensityThreshold,
            senescenceEnabled: true,
            plagueActive: true,
            config);

        Assert.True(result.Base > 0);
        Assert.True(result.DefenseTax > 0);
        Assert.True(result.PlasticityTax > 0);
        Assert.True(result.Crowding > 0);
        Assert.True(result.Senescence > 0);
        Assert.True(result.Plague > 0);
        Assert.Equal(
            result.SelfCostAfterEfficiency + result.ThermalStress + result.Crowding + result.Senescence + result.Plague,
            result.Total,
            precision: 10);
    }
}
