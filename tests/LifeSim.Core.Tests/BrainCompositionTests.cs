using LifeSim.Core.Brains;
using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class BrainCompositionTests
{
    private static WorldState NewWorldState(ulong seed = 2024) => new() { Seed = seed, Width = 64, Height = 64 };

    private static SimulationConfig WithComposition(params BrainTypeSpec[] specs) =>
        SimulationConfig.Default with { FoundingComposition = specs };

    [Fact]
    public void FoundingComposition_seedsExactlyTheRequestedCountsPerType()
    {
        SimulationConfig config = WithComposition(
            new BrainTypeSpec { Name = "Generic", Count = 10 },
            new BrainTypeSpec { Name = "Selfish", Script = BuiltInBrains.Selfish, Count = 7 },
            new BrainTypeSpec { Name = "Aggressor", Script = BuiltInBrains.Aggressor, Count = 5 });

        var world = SimulationWorld.CreateGenesis(NewWorldState(), config);

        Assert.Equal(22, world.Organisms.Count); // 10 + 7 + 5, InitialPopulation ignored

        Dictionary<string, long> byType = world.ToSnapshot().Metrics!.PopulationByFoundingType
            .ToDictionary(p => p.Name, p => p.Count);
        Assert.Equal(10, byType["Generic"]);
        Assert.Equal(7, byType["Selfish"]);
        Assert.Equal(5, byType["Aggressor"]);
    }

    [Fact]
    public void EmptyComposition_fallsBackToInitialPopulationOfGenerics()
    {
        SimulationConfig config = SimulationConfig.Default with { InitialPopulation = 12 };
        var world = SimulationWorld.CreateGenesis(NewWorldState(), config);

        Assert.Equal(12, world.Organisms.Count);
        List<FoundingTypePopulation> byType = world.ToSnapshot().Metrics!.PopulationByFoundingType;
        Assert.Equal("Generic", Assert.Single(byType).Name);
    }

    [Fact]
    public void FoundingType_breedsTrue_throughReproduction()
    {
        // A single-type world: every organism ever born must carry that founding type, even after many
        // ticks of reproduction (the label is inherited, though the brain itself evolves).
        SimulationConfig config = WithComposition(
            new BrainTypeSpec { Name = "Forager", Script = BuiltInBrains.Forager, Count = 30 });
        var world = SimulationWorld.CreateGenesis(NewWorldState(777), config);

        for (int i = 0; i < 60 && !world.Extinct; i++)
        {
            world.Advance();
        }

        List<FoundingTypePopulation> byType = world.ToSnapshot().Metrics!.PopulationByFoundingType;
        Assert.All(byType, p => Assert.Equal("Forager", p.Name)); // no other type ever appears
    }

    [Fact]
    public void FoundingType_survivesSaveAndReload()
    {
        SimulationConfig config = WithComposition(
            new BrainTypeSpec { Name = "Selfless", Script = BuiltInBrains.Selfless, Count = 8 },
            new BrainTypeSpec { Name = "Aggressor", Script = BuiltInBrains.Aggressor, Count = 8 });
        var world = SimulationWorld.CreateGenesis(NewWorldState(), config);
        world.Advance();

        WorldSnapshot snapshot = world.ToSnapshot();
        SimulationWorld reloaded = SimulationWorld.FromSnapshot(SnapshotSerializer.Load(SnapshotSerializer.Save(snapshot)));

        Assert.Equal(
            snapshot.Metrics!.PopulationByFoundingType.Select(p => (p.Name, p.Count)),
            reloaded.ToSnapshot().Metrics!.PopulationByFoundingType.Select(p => (p.Name, p.Count)));
    }

    [Fact]
    public void BadScriptInComposition_failsAtWorldCreationWithAClearMessage()
    {
        SimulationConfig config = WithComposition(
            new BrainTypeSpec { Name = "Broken", Script = "type Broken:\n  prefer HarvestToward(unicorns)", Count = 5 });

        Assert.Throws<BrainScriptException>(() => SimulationWorld.CreateGenesis(NewWorldState(), config));
    }
}
