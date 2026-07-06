using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;
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
    public void Advance_populationCanStillGoExtinct_underResourcePressure()
    {
        // A small population confined to sparse-cap Grassland tiles, with grazing, predation, and
        // reproduction all now live, can still crash to extinction (lifesim.md §17) — this
        // exercises the real Death & Transfer -> extinction path with every Phase 7 mechanic active,
        // not just the vacuous zero-population edge case above.
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

        Assert.Equal(world.LineageRecords.Count, restored.LineageRecords.Count);
    }

    [Fact]
    public void Advance_soleOrganism_eventuallyGrazesForANetEnergyGain()
    {
        // With only one organism in the world, the only possible source of energy gain each tick
        // is Harvest-Self grazing — there's no other organism to predate, and Reproduce only ever
        // spends energy. So any tick-over-tick increase proves grazing actually transferred energy.
        SimulationConfig config = SimulationConfig.Default with { InitialPopulation = 1 };
        var world = SimulationWorld.CreateGenesis(NewWorldState(), config);

        double previousEnergy = world.Organisms.Values.Single().Energy;
        bool observedIncrease = false;

        for (int i = 0; i < 300 && !world.Extinct; i++)
        {
            world.Advance();
            if (world.Extinct)
            {
                break;
            }

            double currentEnergy = world.Organisms.Values.Single().Energy;
            if (currentEnergy > previousEnergy)
            {
                observedIncrease = true;
                break;
            }

            previousEnergy = currentEnergy;
        }

        Assert.True(observedIncrease, "Expected at least one tick where grazing increased the sole organism's energy.");
    }

    [Fact]
    public void Advance_nonPredationDeath_depositsCorpseEnergyOnItsOwnTile()
    {
        // A single organism, engineered so every possible action this tick leaves its energy
        // unchanged going into the Metabolism phase (speed 0 blocks Move; zero ground energy
        // nearby makes Harvest a no-gain graze; energy well under the reproduction cost fails
        // Reproduce) — so it starves by exactly the deterministic metabolism cost, and corpse
        // deposit is exactly predictable (lifesim.md §11).
        var worldState = new WorldState { Seed = 555, Width = 20, Height = 20 };
        SimulationConfig config = SimulationConfig.Default;

        var genome = new Genome
        {
            Size = 2.0,
            SpeedCapacity = 0.0,
            ThermalCenter = 0.0,
            ThermalWidth = 1000.0, // never thermally stressed
            EnvRadius = 0.0,
            OrgRadius = 0.0,
            SensoryAcuity = 1.0, // no sensory noise
        };

        var terrain = new TerrainSampler(worldState.Seed, config);
        const int x = 10, y = 10;
        double tileTemperature = terrain.TemperatureAt(x, y);
        double metabolismCost = Metabolism.Total(genome, tileTemperature, config.Metabolism);

        var brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));
        var organism = new Organism(1, genome, "Test-Test-Organism", metabolismCost, x, y, brain);

        var snapshot = new WorldSnapshot
        {
            World = worldState,
            Configuration = config,
            PrngStreams = PrngStreams.FromSeed(worldState.Seed).CaptureState(),
            EvolutionBookkeeping = new EvolutionBookkeeping { NextOrganismId = 2 },
            GroundEnergy =
            [
                new GroundEnergyEntry(x, y, 0.0),
                new GroundEnergyEntry(x + 1, y, 0.0),
                new GroundEnergyEntry(x - 1, y, 0.0),
                new GroundEnergyEntry(x, y + 1, 0.0),
                new GroundEnergyEntry(x, y - 1, 0.0),
            ],
            Organisms = [OrganismSnapshot.From(organism)],
            Lineages = [new LineageSnapshot { OrganismId = 1, LineageId = 1, BirthTick = 0, GenerationDepth = 0, BirthTraits = GenomeSnapshot.From(genome) }],
            Metrics = new SimulationMetrics { Population = 1, Extinct = false },
        };

        var world = SimulationWorld.FromSnapshot(snapshot);
        world.Advance();

        Assert.True(world.Extinct);

        // The Resource Regeneration Phase (step 7) runs after Death & Transfer (step 6) within the
        // same tick, so one biome regen tick is added on top of the corpse deposit before this
        // Advance() call returns.
        BiomeSettings biomeSettings = config.Biomes.For(terrain.BiomeAt(x, y));
        double expectedCorpseEnergy = Math.Min(
            biomeSettings.EnergyCap, (metabolismCost * config.Events.CorpseEnergyFraction) + biomeSettings.RegenRate);
        GroundEnergyEntry? deposited = world.ToSnapshot().GroundEnergy.SingleOrDefault(e => e.X == x && e.Y == y);
        Assert.NotNull(deposited);
        Assert.Equal(expectedCorpseEnergy, deposited.Energy, precision: 10);

        LineageEntry lineage = world.LineageRecords[1];
        Assert.Equal(1, lineage.DeathTick);
    }

    [Fact]
    public void Advance_populationGrows_viaReproduction_whenEnergyIsAbundant()
    {
        SimulationConfig config = SimulationConfig.Default with { InitialPopulation = 10 };
        var world = SimulationWorld.CreateGenesis(NewWorldState(seed: 909090), config);
        int initialPopulation = world.Organisms.Count;

        for (int i = 0; i < 200 && !world.Extinct; i++)
        {
            world.Advance();
            foreach (Organism organism in world.Organisms.Values)
            {
                Assert.True(organism.Energy <= Organism.EnergyCeiling);
            }
        }

        Assert.True(world.LineageRecords.Count > initialPopulation, "Expected at least one birth to have occurred.");

        // Every non-founder lineage record must point at a real ancestor with a sane generation depth.
        foreach (LineageEntry entry in world.LineageRecords.Values)
        {
            if (entry.ParentId is null)
            {
                Assert.Equal(0, entry.GenerationDepth);
                Assert.Equal(entry.OrganismId, entry.LineageId);
            }
            else
            {
                Assert.True(world.LineageRecords.ContainsKey(entry.ParentId.Value));
                Assert.True(entry.GenerationDepth > world.LineageRecords[entry.ParentId.Value].GenerationDepth);
            }
        }
    }
}
