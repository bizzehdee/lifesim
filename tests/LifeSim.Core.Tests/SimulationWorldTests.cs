using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Events;
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
        // The tuned default config (Phase 12) is deliberately survivable, so this exercises the real
        // Death & Transfer -> extinction path under an explicitly harsh metabolism: base cost per
        // size is cranked far above what grazing can replace, so the whole population starves out.
        SimulationConfig harsh = SmallPopulation() with
        {
            Metabolism = new MetabolismConfig { BaseMetabolismPerSize = 5.0 },
        };
        var world = SimulationWorld.CreateGenesis(NewWorldState(), harsh);

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

    [Fact]
    public void Advance_overALongRun_driftsTraitsAndGrowsBrainTopology_viaMutation()
    {
        // Crank structural/trait mutation up so growth and drift are visible within the run budget.
        SimulationConfig config = SimulationConfig.Default with
        {
            InitialPopulation = 10,
            Mutation = new MutationConfig
            {
                TraitMutationRate = 1.0,
                TraitMutationDelta = 0.1,
                WeightMutationRate = 0.8,
                WeightMutationPower = 0.5,
                ConnectionMutationRate = 0.5,
                NodeMutationRate = 0.5,
            },
        };

        var world = SimulationWorld.CreateGenesis(NewWorldState(seed: 909090), config);
        int genesisNodeCount = NeatTopology.InputCount + NeatTopology.OutputCount;

        for (int i = 0; i < 200 && !world.Extinct; i++)
        {
            world.Advance();
        }

        Assert.True(world.LineageRecords.Count > config.InitialPopulation, "Expected at least one birth to have occurred.");

        // The innovation counter only advances on a real structural mutation (lifesim.md §4, §8).
        Assert.True(
            world.ToSnapshot().EvolutionBookkeeping.NextInnovationId > NeatTopology.ReservedInnovationIdCount,
            "Expected at least one structural brain mutation to have advanced the innovation counter.");

        // Brain topology grew beyond the genesis (no-hidden-node) layout for at least one organism.
        Assert.Contains(world.Organisms.Values, o => o.Brain.Nodes.Count > genesisNodeCount);

        // Trait distributions drifted: founders are all identical mid-range, so any variation in the
        // birth-trait record proves offspring diverged.
        int distinctBirthSizes = world.LineageRecords.Values
            .Select(e => Math.Round(e.BirthTraits.Size, 6))
            .Distinct()
            .Count();
        Assert.True(distinctBirthSizes > 1, "Expected mutation to spread trait values across lineages.");
    }

    // --- Phase 9: environmental stochasticity & events (lifesim.md §6) ---

    private static EventsConfig NoEvents() => new EventsConfig() with
    {
        BlightProbability = 0.0,
        PlagueProbability = 0.0,
        TemperatureAnomalyProbability = 0.0,
    };

    private static Genome InertGenome() => new()
    {
        Size = 2.0,
        SpeedCapacity = 0.0,   // cannot move → no locomotion tax, position is fixed
        ThermalCenter = 0.0,
        ThermalWidth = 1000.0, // never thermally stressed
        EnvRadius = 0.0,
        OrgRadius = 0.0,
        SensoryAcuity = 0.0,   // sensory tax = 0 (noise is irrelevant to energy)
    };

    private static Organism InertOrganism(long id, int x, int y, double energy) =>
        new(id, InertGenome(), $"Test-Test-Organism{id}", energy, x, y,
            NeatGenomeFactory.CreateMinimalFullyConnected(new Prng((ulong)id + 1)));

    [Fact]
    public void Advance_resourceBlight_suspendsGroundEnergyRegeneration()
    {
        var worldState = new WorldState { Seed = 555, Width = 30, Height = 30 };
        var terrain = new TerrainSampler(worldState.Seed, SimulationConfig.Default);

        // A tile with positive regen to observe, and a separate tile to park the (immobile) organism.
        (int X, int Y) observed = FindTile(terrain, t => t is Biome.Grassland or Biome.Swamp);
        (int X, int Y) parked = FindTile(terrain, _ => true, exclude: observed);

        double RegenObservedTileAfterOneTick(double blightProbability)
        {
            var config = SimulationConfig.Default with
            {
                Events = NoEvents() with { BlightProbability = blightProbability, BlightDurationTicks = 10 },
            };
            var snapshot = new WorldSnapshot
            {
                World = worldState,
                Configuration = config,
                PrngStreams = PrngStreams.FromSeed(worldState.Seed).CaptureState(),
                EvolutionBookkeeping = new EvolutionBookkeeping { NextOrganismId = 2 },
                GroundEnergy = [new GroundEnergyEntry(observed.X, observed.Y, 0.0)], // drained, so regen is visible
                Organisms = [OrganismSnapshot.From(InertOrganism(1, parked.X, parked.Y, energy: 50.0))],
                Lineages = [new LineageSnapshot { OrganismId = 1, LineageId = 1, BirthTick = 0, GenerationDepth = 0, BirthTraits = GenomeSnapshot.From(InertGenome()) }],
                Metrics = new SimulationMetrics { Population = 1, Extinct = false },
            };

            var world = SimulationWorld.FromSnapshot(snapshot);
            world.Advance();

            // The tile starts drained (0) and its biome regen keeps it below cap, so it stays a
            // tracked override in both runs — present whether or not it regenerated.
            return world.ToSnapshot().GroundEnergy.Single(e => e.X == observed.X && e.Y == observed.Y).Energy;
        }

        double withoutBlight = RegenObservedTileAfterOneTick(0.0);
        double withBlight = RegenObservedTileAfterOneTick(1.0);

        Assert.True(withoutBlight > 0.0, "Without blight, the drained tile should regenerate.");
        Assert.Equal(0.0, withBlight); // blight halts regen, so the drained tile stays empty
    }

    [Fact]
    public void Advance_climaticAnomaly_raisesMetabolicCostViaThermalStress()
    {
        var worldState = new WorldState { Seed = 555, Width = 30, Height = 30 };
        var terrain = new TerrainSampler(worldState.Seed, SimulationConfig.Default);
        (int X, int Y) tile = FindTile(terrain, _ => true);

        // A narrow thermal envelope centered on the tile's true °C temperature: zero stress normally,
        // but a ±20°C anomaly pushes the tile outside the envelope, so metabolism costs more.
        double baseTemp = terrain.TemperatureCelsiusAt(tile.X, tile.Y);
        var genome = InertGenome() with { ThermalCenter = baseTemp, ThermalWidth = 2.0 };

        double EnergyAfterOneTick(double anomalyProbability)
        {
            var config = SimulationConfig.Default with
            {
                Events = NoEvents() with
                {
                    TemperatureAnomalyProbability = anomalyProbability,
                    TemperatureAnomalyDurationTicks = 10,
                    TemperatureAnomalyMagnitude = 20.0,
                },
            };
            var organism = new Organism(1, genome, "Test-Test-Organism", 50.0, tile.X, tile.Y,
                NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(2)));
            var snapshot = new WorldSnapshot
            {
                World = worldState,
                Configuration = config,
                PrngStreams = PrngStreams.FromSeed(worldState.Seed).CaptureState(),
                EvolutionBookkeeping = new EvolutionBookkeeping { NextOrganismId = 2 },
                // Drain the tile and its orthogonal neighbors so no Harvest can add energy.
                GroundEnergy = DrainedCross(tile),
                Organisms = [OrganismSnapshot.From(organism)],
                Lineages = [new LineageSnapshot { OrganismId = 1, LineageId = 1, BirthTick = 0, GenerationDepth = 0, BirthTraits = GenomeSnapshot.From(genome) }],
                Metrics = new SimulationMetrics { Population = 1, Extinct = false },
            };

            var world = SimulationWorld.FromSnapshot(snapshot);
            world.Advance();
            return world.Organisms[1].Energy;
        }

        double calm = EnergyAfterOneTick(0.0);
        double heatwave = EnergyAfterOneTick(1.0);

        Assert.True(heatwave < calm, "A climatic anomaly should raise metabolic cost, leaving less energy.");
    }

    [Fact]
    public void Advance_densityPlague_drainsCrowdedOrganismsByTheConfiguredAmount()
    {
        var worldState = new WorldState { Seed = 555, Width = 30, Height = 30 };
        const double drain = 3.0;
        const int cx = 10, cy = 10;

        // Two diagonally-adjacent organisms: each sits in the other's 3×3 density window (density 2),
        // but the Move/Harvest actions are orthogonal-only, so they can never interact — no combat,
        // no movement. With every reachable tile drained and energy below the reproduction cost, the
        // only per-tick energy change is metabolism (identical in both runs) plus the plague drain.
        double CenterEnergyAfterOneTick(double plagueProbability)
        {
            var config = SimulationConfig.Default with
            {
                Events = NoEvents() with
                {
                    PlagueProbability = plagueProbability,
                    PlagueDurationTicks = 10,
                    PlagueDensityThreshold = 2,
                    PlagueEnergyDrainPerTick = drain,
                },
            };

            Organism a = InertOrganism(1, cx, cy, energy: 15.0);
            Organism b = InertOrganism(2, cx + 1, cy + 1, energy: 15.0);
            var snapshot = new WorldSnapshot
            {
                World = worldState,
                Configuration = config,
                PrngStreams = PrngStreams.FromSeed(worldState.Seed).CaptureState(),
                EvolutionBookkeeping = new EvolutionBookkeeping { NextOrganismId = 3 },
                GroundEnergy = [.. DrainedCross((cx, cy)), .. DrainedCross((cx + 1, cy + 1))],
                Organisms = [OrganismSnapshot.From(a), OrganismSnapshot.From(b)],
                Lineages =
                [
                    new LineageSnapshot { OrganismId = 1, LineageId = 1, BirthTick = 0, GenerationDepth = 0, BirthTraits = GenomeSnapshot.From(InertGenome()) },
                    new LineageSnapshot { OrganismId = 2, LineageId = 2, BirthTick = 0, GenerationDepth = 0, BirthTraits = GenomeSnapshot.From(InertGenome()) },
                ],
                Metrics = new SimulationMetrics { Population = 2, Extinct = false },
            };

            var world = SimulationWorld.FromSnapshot(snapshot);
            world.Advance();
            return world.Organisms[1].Energy;
        }

        double calm = CenterEnergyAfterOneTick(0.0);
        double plagued = CenterEnergyAfterOneTick(1.0);

        Assert.Equal(drain, calm - plagued, precision: 10);
    }

    [Fact]
    public void Advance_activeEventModifiers_roundTripThroughASnapshot()
    {
        // A world where every event fires each tick, so a modifier of each type is active.
        var config = SimulationConfig.Default with
        {
            InitialPopulation = 5,
            Events = new EventsConfig() with
            {
                BlightProbability = 1.0,
                PlagueProbability = 1.0,
                TemperatureAnomalyProbability = 1.0,
            },
        };

        var world = SimulationWorld.CreateGenesis(NewWorldState(seed: 123456), config);
        world.Advance();

        WorldSnapshot snapshot = world.ToSnapshot();
        Assert.Equal(3, snapshot.EnvironmentModifiers.Count);
        Assert.Contains(snapshot.EnvironmentModifiers, m => m.Type == EventType.ResourceBlight);
        Assert.Contains(snapshot.EnvironmentModifiers, m => m.Type == EventType.ClimaticAnomaly);

        WorldSnapshot reloaded = SnapshotSerializer.Load(SnapshotSerializer.Save(snapshot));
        Assert.Equal(snapshot.EnvironmentModifiers, reloaded.EnvironmentModifiers);
    }

    private static List<GroundEnergyEntry> DrainedCross((int X, int Y) center) =>
    [
        new GroundEnergyEntry(center.X, center.Y, 0.0),
        new GroundEnergyEntry(center.X, center.Y - 1, 0.0),
        new GroundEnergyEntry(center.X, center.Y + 1, 0.0),
        new GroundEnergyEntry(center.X - 1, center.Y, 0.0),
        new GroundEnergyEntry(center.X + 1, center.Y, 0.0),
    ];

    private static (int X, int Y) FindTile(TerrainSampler terrain, Func<Biome, bool> predicate, (int X, int Y)? exclude = null)
    {
        for (int y = 0; y < 30; y++)
        {
            for (int x = 0; x < 30; x++)
            {
                if ((x, y) != exclude && predicate(terrain.BiomeAt(x, y)))
                {
                    return (x, y);
                }
            }
        }

        throw new InvalidOperationException("No tile matched the predicate in the search window.");
    }
}
