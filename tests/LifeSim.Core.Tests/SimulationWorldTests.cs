using LifeSim.Core.Configuration;
using LifeSim.Core.Organisms;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class SimulationWorldTests
{
    private static WorldState NewWorldState(ulong seed = 2024) => new() { Seed = seed, Width = 64, Height = 64 };

    private static SimulationConfig SmallPopulation() => SimulationConfig.Default with { InitialPopulation = 20 };

    [Fact]
    public void CreateGenesis_placesInitialPopulationOnGrasslandTiles_withNoOverlap()
    {
        SimulationConfig config = SmallPopulation();
        var world = SimulationWorld.CreateGenesis(NewWorldState(), config);

        Assert.Equal(config.InitialPopulation, world.Organisms.Count);
        Assert.False(world.Extinct);

        var terrain = new TerrainSampler(world.World.Seed, config);
        var seenTiles = new HashSet<(int X, int Y)>();
        foreach (Organism organism in world.Organisms.Values)
        {
            Assert.Equal(Biome.Grassland, terrain.BiomeAt(organism.X, organism.Y));
            Assert.True(seenTiles.Add((organism.X, organism.Y)), "Two organisms occupy the same tile.");
            Assert.Equal(Organism.EnergyCeiling, organism.Energy);
        }
    }

    [Fact]
    public void Advance_incrementsTick()
    {
        var world = SimulationWorld.CreateGenesis(NewWorldState(), SmallPopulation());
        world.Advance();
        world.Advance();
        Assert.Equal(2, world.Tick);
    }

    [Fact]
    public void Advance_neverPlacesTwoOrganismsOnTheSameTile()
    {
        var world = SimulationWorld.CreateGenesis(NewWorldState(), SmallPopulation());

        for (int i = 0; i < 25; i++)
        {
            world.Advance();
            if (world.Extinct)
            {
                break;
            }

            var seenTiles = new HashSet<(int X, int Y)>();
            foreach (Organism organism in world.Organisms.Values)
            {
                Assert.True(seenTiles.Add((organism.X, organism.Y)), "Two organisms occupy the same tile.");
            }
        }
    }

    [Fact]
    public void Advance_staysWithinWorldBounds()
    {
        var world = SimulationWorld.CreateGenesis(NewWorldState(), SmallPopulation());

        for (int i = 0; i < 25 && !world.Extinct; i++)
        {
            world.Advance();
            foreach (Organism organism in world.Organisms.Values)
            {
                Assert.InRange(organism.X, 0, world.World.Width - 1);
                Assert.InRange(organism.Y, 0, world.World.Height - 1);
            }
        }
    }

    [Fact]
    public void Advance_throwsOnceExtinct()
    {
        // A world with no organisms is vacuously extinct at genesis.
        SimulationConfig config = SimulationConfig.Default with { InitialPopulation = 0 };
        var world = SimulationWorld.CreateGenesis(NewWorldState(), config);

        Assert.True(world.Extinct);
        Assert.Throws<InvalidOperationException>(world.Advance);
    }

    [Fact]
    public void Advance_eventuallyStarvesToExtinction_sinceHarvestIsStubbedUntilPhase7()
    {
        // Phase 4 has no ambient grazing yet, so a population that only ever spends energy must
        // die out; this exercises the real Death & Transfer -> extinction path, not just the
        // vacuous zero-population edge case above (lifesim.md §17).
        var world = SimulationWorld.CreateGenesis(NewWorldState(), SmallPopulation());

        for (int i = 0; i < 500 && !world.Extinct; i++)
        {
            world.Advance();
        }

        Assert.True(world.Extinct);
        Assert.Empty(world.Organisms);
        Assert.Throws<InvalidOperationException>(world.Advance);
    }

    [Fact]
    public void ToSnapshot_thenFromSnapshot_preservesOrganismsAndTick()
    {
        var world = SimulationWorld.CreateGenesis(NewWorldState(), SmallPopulation());
        world.Advance();
        world.Advance();
        world.Advance();

        WorldSnapshot snapshot = world.ToSnapshot();
        var restored = SimulationWorld.FromSnapshot(snapshot);

        Assert.Equal(world.Tick, restored.Tick);
        Assert.Equal(world.Extinct, restored.Extinct);
        Assert.Equal(world.Organisms.Count, restored.Organisms.Count);
        foreach ((long id, Organism organism) in world.Organisms)
        {
            Organism restoredOrganism = restored.Organisms[id];
            Assert.Equal(organism.X, restoredOrganism.X);
            Assert.Equal(organism.Y, restoredOrganism.Y);
            Assert.Equal(organism.Energy, restoredOrganism.Energy);
            Assert.Equal(organism.Age, restoredOrganism.Age);
            Assert.Equal(organism.Name, restoredOrganism.Name);
            Assert.Equal(organism.LastAction, restoredOrganism.LastAction);
        }
    }
}
