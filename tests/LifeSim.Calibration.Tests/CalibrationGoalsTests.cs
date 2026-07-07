using LifeSim.Core.Configuration;
using LifeSim.Core.Snapshot;

namespace LifeSim.Calibration.Tests;

/// <summary>
/// The calibration goals the default configuration must satisfy (lifesim.md §15): the tuned
/// defaults avoid the failure modes that a badly-balanced coupled-constant set would exhibit.
/// </summary>
public class CalibrationGoalsTests
{
    [Fact]
    public void DefaultConfig_avoidsEarlyExtinctionAndUnboundedGrowth()
    {
        using var harness = new ScenarioHarness();
        WorldSnapshot genesis = harness.Genesis(seed: 42, width: 64, height: 64, population: 40);

        RunResult result = harness.Run(genesis, ticks: 200);

        // Never extinct — and certainly not in the first few ticks.
        Assert.All(result.Populations, p => Assert.True(p > 0, "Population went extinct."));
        // No runaway growth without resource pressure.
        Assert.True(result.Populations.Max() <= 40 * 6, $"Population grew unbounded to {result.Populations.Max()}.");
    }

    [Fact]
    public void DefaultConfig_maintainsTraitDiversity_noSingleTraitValueDominates()
    {
        using var harness = new ScenarioHarness();
        WorldSnapshot genesis = harness.Genesis(seed: 42, width: 64, height: 64, population: 40);

        RunResult result = harness.Run(genesis, ticks: 150);

        // Founders are all identical mid-range; mutation across surviving offspring must spread the
        // distribution, so no single trait value ends up dominating the whole population.
        int distinctSizes = result.Final.Organisms.Select(o => Math.Round(o.Genome.Size, 4)).Distinct().Count();
        Assert.True(distinctSizes > 1, "Expected trait mutation to spread Size across the population.");
    }

    [Fact]
    public void DefaultConfig_keepsPredationAndReproductionCostly()
    {
        SimulationConfig config = SimulationConfig.Default;

        // No cost-free predators: failure is penalised and larger bodies cost more baseline upkeep.
        Assert.True(config.MovementCombat.FailedCombatPenalty > 0.0);
        Assert.True(config.Metabolism.BaseMetabolismPerSize > 0.0);

        // No constraint-free reproduction loops: reproduction costs energy and is a net sink
        // (the offspring receives strictly less than the parent pays).
        Assert.True(config.Reproduction.ReproductionBaseCost > 0.0);
        Assert.True(config.Reproduction.OffspringEnergyFraction < 1.0);
    }
}
