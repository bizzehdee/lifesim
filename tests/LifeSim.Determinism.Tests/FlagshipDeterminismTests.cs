using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Determinism.Tests;

/// <summary>
/// The two flagship regression tests the whole architecture rests on (lifesim.md §15). From
/// Phase 4 onward, every merge is gated on these staying green.
/// </summary>
public class FlagshipDeterminismTests
{
    private const int Ticks = 100;

    private static WorldState NewWorldState() => new() { Seed = 20240607, Width = 48, Height = 48 };

    private static SimulationConfig NewConfig() => SimulationConfig.Default with { InitialPopulation = 30 };

    /// <summary>Same seed, N ticks, run twice, must produce byte-identical final snapshots.</summary>
    [Fact]
    public void SeedReplay_sameSeed_producesByteIdenticalSnapshots()
    {
        WorldSnapshot a = RunTicks(SimulationWorld.CreateGenesis(NewWorldState(), NewConfig()), Ticks);
        WorldSnapshot b = RunTicks(SimulationWorld.CreateGenesis(NewWorldState(), NewConfig()), Ticks);

        string jsonA = SnapshotSerializer.Save(a);
        string jsonB = SnapshotSerializer.Save(b);

        Assert.Equal(jsonA, jsonB);
    }

    /// <summary>100 ticks straight must match 50 ticks + serialize + reload + 50 more ticks.</summary>
    [Fact]
    public void SaveReloadEquivalence_matchesAnUninterruptedRun()
    {
        WorldSnapshot straightThrough = RunTicks(SimulationWorld.CreateGenesis(NewWorldState(), NewConfig()), Ticks);

        SimulationWorld halfway = SimulationWorld.CreateGenesis(NewWorldState(), NewConfig());
        RunTicks(halfway, Ticks / 2);
        SimulationWorld resumed = SimulationWorld.FromSnapshot(halfway.ToSnapshot());
        WorldSnapshot resumedResult = RunTicks(resumed, Ticks / 2);

        Assert.Equal(SnapshotSerializer.Save(straightThrough), SnapshotSerializer.Save(resumedResult));
    }

    /// <summary>
    /// Node <c>state</c> is dynamic organism state, not just weights (lifesim.md §4, §12) — the
    /// save/reload test above already round-trips it implicitly, but this asserts it directly:
    /// after a few ticks, at least one brain has non-zero recurrent state, and it survives
    /// serialization exactly.
    /// </summary>
    [Fact]
    public void RecurrentNodeState_isNonZeroAfterTicking_andRoundTripsExactly()
    {
        SimulationWorld world = SimulationWorld.CreateGenesis(NewWorldState(), NewConfig());
        for (int i = 0; i < 5 && !world.Extinct; i++)
        {
            world.Advance();
        }

        WorldSnapshot snapshot = world.ToSnapshot();
        Assert.Contains(snapshot.Organisms, o => o.Brain.Nodes.Any(n => n.State != 0.0));

        WorldSnapshot reloaded = SnapshotSerializer.Load(SnapshotSerializer.Save(snapshot));
        Assert.Equal(snapshot.Organisms, reloaded.Organisms);
    }

    /// <summary>
    /// Both flagship guarantees must also hold while stochastic events fire frequently (lifesim.md
    /// §6, §9). Default probabilities (0.001) rarely trigger inside a 100-tick run, so this pins
    /// them high enough that blights, plagues, and anomalies are active throughout — the events
    /// stream, active modifiers, and their effects all have to round-trip exactly.
    /// </summary>
    [Fact]
    public void Determinism_holds_whileEventsFireFrequently()
    {
        static SimulationConfig EventfulConfig() => NewConfig() with
        {
            Events = new EventsConfig() with
            {
                BlightProbability = 0.1,
                PlagueProbability = 0.1,
                TemperatureAnomalyProbability = 0.1,
                PlagueDensityThreshold = 2,
            },
        };

        // Seed replay.
        WorldSnapshot a = RunTicks(SimulationWorld.CreateGenesis(NewWorldState(), EventfulConfig()), Ticks);
        WorldSnapshot b = RunTicks(SimulationWorld.CreateGenesis(NewWorldState(), EventfulConfig()), Ticks);
        Assert.Equal(SnapshotSerializer.Save(a), SnapshotSerializer.Save(b));

        // At least one event actually fired during the run, so this really exercises the machinery.
        Assert.NotEmpty(a.EnvironmentModifiers);

        // Save/reload equivalence.
        SimulationWorld halfway = SimulationWorld.CreateGenesis(NewWorldState(), EventfulConfig());
        RunTicks(halfway, Ticks / 2);
        SimulationWorld resumed = SimulationWorld.FromSnapshot(halfway.ToSnapshot());
        WorldSnapshot resumedResult = RunTicks(resumed, Ticks / 2);
        Assert.Equal(SnapshotSerializer.Save(a), SnapshotSerializer.Save(resumedResult));
    }

    /// <summary>
    /// Multithreading is an execution knob, not a simulation input (lifesim.md §7): the parallelised
    /// brain forward pass is a pure function and the softmax roll stays sequential, so a run must be
    /// byte-identical for any thread count. This is the safety net for the whole parallelisation.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void ThreadCount_doesNotChangeTheResult(int threads)
    {
        WorldSnapshot serial = RunTicks(SimulationWorld.CreateGenesis(NewWorldState(), NewConfig()), Ticks);

        SimulationWorld parallelWorld = SimulationWorld.CreateGenesis(NewWorldState(), NewConfig());
        parallelWorld.MaxDegreeOfParallelism = threads;
        WorldSnapshot parallel = RunTicks(parallelWorld, Ticks);

        Assert.Equal(SnapshotSerializer.Save(serial), SnapshotSerializer.Save(parallel));
    }

    private static WorldSnapshot RunTicks(SimulationWorld world, int ticks)
    {
        for (int i = 0; i < ticks && !world.Extinct; i++)
        {
            world.Advance();
        }

        return world.ToSnapshot();
    }
}
