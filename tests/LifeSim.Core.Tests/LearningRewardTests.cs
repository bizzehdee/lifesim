using LifeSim.Core.Configuration;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class LearningRewardTests
{
    private static readonly LearningConfig Config = new();

    [Fact]
    public void IdleHasNoReward_whenOnlyUnavoidableMetabolismWasPaid()
    {
        double reward = LearningReward.Calculate(
            OrganismAction.Idle, ActionResult.Success, actionEnergyDelta: 0.0, locomotionCost: 0.0, Config);

        Assert.Equal(0.0, reward);
    }

    [Fact]
    public void SuccessfulHarvestCreditsItsOwnEnergyGain()
    {
        double reward = LearningReward.Calculate(
            OrganismAction.HarvestSelf, ActionResult.Success, actionEnergyDelta: 4.0, locomotionCost: 0.0, Config);

        Assert.Equal(0.45, reward, precision: 10);
    }

    [Fact]
    public void MovementPaysOnlyItsLocomotionCost_notBasalOrThermalCosts()
    {
        double reward = LearningReward.Calculate(
            OrganismAction.MoveEast, ActionResult.Success, actionEnergyDelta: 0.0, locomotionCost: 1.5, Config);

        Assert.Equal(-0.1, reward, precision: 10);
    }

    [Fact]
    public void FailedChoiceGetsFeedbackWithoutMetabolicNoise()
    {
        double reward = LearningReward.Calculate(
            OrganismAction.MoveNorth, ActionResult.Blocked, actionEnergyDelta: 0.0, locomotionCost: 0.0, Config);

        Assert.Equal(-Config.FailedActionPenalty, reward);
    }

    [Fact]
    public void ReproductionIsCreditedAsFitness_notPunishedAsEnergyLoss()
    {
        double reward = LearningReward.Calculate(
            OrganismAction.Reproduce, ActionResult.Success, actionEnergyDelta: -30.0, locomotionCost: 0.0, Config);

        Assert.Equal(Config.ReproductionReward, reward);
    }

    [Theory]
    [InlineData(1000.0, 1.0)]
    [InlineData(-1000.0, -1.0)]
    public void RewardIsBounded(double energyDelta, double expected)
    {
        double reward = LearningReward.Calculate(
            OrganismAction.HarvestSelf, ActionResult.Success, energyDelta, locomotionCost: 0.0, Config);

        Assert.Equal(expected, reward);
    }
}
