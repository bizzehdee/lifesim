using LifeSim.Core.Configuration;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class ReproductionRulesTests
{
    [Fact]
    public void Assess_usesMorphologyFertilityMassEnergyAndCooldownTogether()
    {
        SimulationConfig config = SimulationConfig.Default;
        Genome genome = new()
        {
            Size = 2,
            CellCount = 3,
            GermWeight = 1,
            FeederWeight = 1,
            StoreWeight = 1,
            DefenderWeight = 1,
            MoverWeight = 1,
            SensorWeight = 1,
        };
        double expectedCost = config.Reproduction.ReproductionBaseCost
            * Morphology.ReproductionMass(genome, config.Multicellular);

        ReproductionReadiness coolingDown = ReproductionRules.Assess(
            genome, expectedCost, lastBirthTick: 99, currentTick: 100, config);

        Assert.Equal(expectedCost, coolingDown.EnergyCost, precision: 10);
        Assert.True(coolingDown.Fertile);
        Assert.True(coolingDown.HasEnergy);
        Assert.False(coolingDown.OffCooldown);
        Assert.False(coolingDown.IsReady);

        ReproductionReadiness ready = ReproductionRules.Assess(
            genome, expectedCost, lastBirthTick: null, currentTick: 100, config);
        Assert.True(ready.IsReady);
    }
}
