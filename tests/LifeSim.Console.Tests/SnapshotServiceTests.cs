using System.Text.Json;
using LifeSim.Console.Serve;
using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Console.Tests;

public class SnapshotServiceTests
{
    private static SnapshotService NewService(ulong seed = 5, int population = 10)
    {
        var world = SimulationWorld.CreateGenesis(
            new WorldState { Seed = seed, Width = 32, Height = 32 },
            SimulationConfig.Default with { InitialPopulation = population });
        return new SnapshotService(world);
    }

    [Fact]
    public void AdvanceOnce_incrementsTheTick()
    {
        SnapshotService service = NewService();
        Assert.Equal(0, service.Tick);
        Assert.True(service.AdvanceOnce());
        Assert.Equal(1, service.Tick);
    }

    [Fact]
    public void CurrentSnapshotJson_isLoadable()
    {
        SnapshotService service = NewService();
        WorldSnapshot snapshot = SnapshotSerializer.Load(service.CurrentSnapshotJson());
        Assert.Equal(0, snapshot.Tick);
    }

    [Fact]
    public void CurrentMetricsLine_isASingleParsableJsonLine()
    {
        SnapshotService service = NewService();
        string line = service.CurrentMetricsLine();
        Assert.DoesNotContain('\n', line);
        using var doc = JsonDocument.Parse(line);
        Assert.True(doc.RootElement.TryGetProperty("metrics", out _));
    }

    [Fact]
    public void Import_replacesTheRunningWorld()
    {
        SnapshotService service = NewService(seed: 5);
        service.AdvanceOnce();
        Assert.Equal(1, service.Tick);

        var other = SimulationWorld.CreateGenesis(
            new WorldState { Seed = 99, Width = 32, Height = 32 },
            SimulationConfig.Default with { InitialPopulation = 5 });
        service.Import(SnapshotSerializer.Save(other.ToSnapshot()));

        Assert.Equal(0, service.Tick);
        Assert.Equal(99UL, SnapshotSerializer.Load(service.CurrentSnapshotJson()).World.Seed);
    }

    [Fact]
    public void Import_rejectsAMalformedSnapshot()
    {
        SnapshotService service = NewService();
        Assert.Throws<SnapshotValidationException>(() => service.Import("{ not a snapshot }"));
    }
}
