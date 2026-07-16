using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class DecisionTraceTests
{
    [Fact]
    public void Advance_recordsReplayableDecisionTraceForEverySurvivor()
    {
        SimulationWorld world = SimulationWorld.CreateGenesis(
            new WorldState { Seed = 42, Width = 48, Height = 48 },
            SimulationConfig.Default with { InitialPopulation = 12 });

        world.Advance();
        WorldSnapshot snapshot = world.ToSnapshot();

        List<OrganismSnapshot> decided = snapshot.Organisms.Where(organism => organism.LastAction is not null).ToList();
        Assert.NotEmpty(decided);
        Assert.All(decided, organism =>
        {
            Assert.NotNull(organism.DecisionTrace);
            Assert.Equal(snapshot.Tick, organism.DecisionTrace!.Tick);
            Assert.Equal(organism.LastAction, organism.DecisionTrace.ChosenAction);
            Assert.Equal(1.0, organism.DecisionTrace.ActionProbabilities.Sum(), precision: 10);
            Assert.NotNull(organism.DecisionTrace.LearningReward);
            Assert.InRange(organism.DecisionTrace.StrongestInputs.Count, 1, 5);
        });

        WorldSnapshot loaded = SnapshotSerializer.Load(SnapshotSerializer.Save(snapshot));
        long tracedId = decided[0].OrganismId;
        Assert.Equal(
            decided[0].DecisionTrace,
            loaded.Organisms.Single(organism => organism.OrganismId == tracedId).DecisionTrace);
    }
}
