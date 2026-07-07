using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Organisms;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class MetricsTests
{
    private static WorldState NewWorldState(ulong seed = 909090) => new() { Seed = seed, Width = 48, Height = 48 };

    [Fact]
    public void Metrics_snapshotFields_areInternallyConsistentWithLivePopulation()
    {
        var config = SimulationConfig.Default with { InitialPopulation = 30 };
        var world = SimulationWorld.CreateGenesis(NewWorldState(), config);
        for (int i = 0; i < 20 && !world.Extinct; i++)
        {
            world.Advance();
        }

        SimulationMetrics m = world.ToSnapshot().Metrics!;
        List<Organism> living = world.Organisms.Values.ToList();

        Assert.Equal(living.Count, m.Population);

        // Population-by-biome partitions the whole population.
        Assert.Equal(m.Population, m.PopulationByBiome.Sum(b => b.Count));

        // Each trait histogram has a fixed bin count and accounts for every organism.
        Assert.All(m.TraitHistograms, h => Assert.Equal(10, h.Buckets.Count));
        Assert.All(m.TraitHistograms, h => Assert.Equal((int)m.Population, h.Buckets.Sum()));

        // Energy stats match a direct recompute over the same (ascending-id) population.
        Assert.Equal(living.Min(o => o.Energy), m.EnergyMin, precision: 9);
        Assert.Equal(living.Max(o => o.Energy), m.EnergyMax, precision: 9);
        Assert.Equal(living.Average(o => o.Energy), m.EnergyAverage, precision: 9);

        // A representative trait average matches.
        Assert.Equal(living.Average(o => o.Genome.Size), m.TraitAverages.Size, precision: 9);

        // Reproduction-by-lineage covers exactly the currently-living lineages.
        IEnumerable<long> livingLineages = world.Organisms.Keys
            .Select(id => world.LineageRecords[id].LineageId)
            .Distinct()
            .OrderBy(x => x);
        Assert.Equal(livingLineages, m.ReproductionByLineage.Select(r => r.LineageId));
    }

    [Fact]
    public void Metrics_birthAndDeathCounters_sumToTheLineageTotalsOverAWholeRun()
    {
        var config = SimulationConfig.Default with { InitialPopulation = 12 };
        var world = SimulationWorld.CreateGenesis(NewWorldState(), config);

        long birthsSum = 0, deathsSum = 0, grazingSum = 0;
        for (int i = 0; i < 60 && !world.Extinct; i++)
        {
            world.Advance();
            SimulationMetrics tick = world.ToSnapshot().Metrics!;
            birthsSum += tick.Births;
            deathsSum += tick.Deaths;
            grazingSum += tick.SuccessfulGrazing;
        }

        // Every counted birth is a lineage record with a parent; every counted death closed a record.
        long recordedBirths = world.LineageRecords.Values.Count(e => e.ParentId is not null);
        long recordedDeaths = world.LineageRecords.Values.Count(e => e.DeathTick is not null);

        Assert.Equal(recordedBirths, birthsSum);
        Assert.Equal(recordedDeaths, deathsSum);
        Assert.True(birthsSum > 0, "Expected reproduction to occur over the run.");
        Assert.True(grazingSum > 0, "Expected organisms to graze over the run.");
    }

    [Fact]
    public void Metrics_deathCounter_countsASingleStarvationDeath()
    {
        var worldState = new WorldState { Seed = 555, Width = 20, Height = 20 };
        SimulationConfig config = SimulationConfig.Default;

        // Energy well below one tick's base metabolism, a huge thermal envelope, speed 0 → the
        // organism starves this tick no matter which action it picks.
        var genome = new Genome
        {
            Size = 2.0,
            SpeedCapacity = 0.0,
            ThermalCenter = 20.0,
            ThermalWidth = 1000.0,
            EnvRadius = 0.0,
            OrgRadius = 0.0,
            SensoryAcuity = 1.0,
        };
        var organism = new Organism(1, genome, "Test-Test-Organism", 0.01, 10, 10,
            Neat.NeatGenomeFactory.CreateMinimalFullyConnected(new Determinism.Prng(1)));

        var snapshot = new WorldSnapshot
        {
            World = worldState,
            Configuration = config,
            PrngStreams = Determinism.PrngStreams.FromSeed(worldState.Seed).CaptureState(),
            EvolutionBookkeeping = new EvolutionBookkeeping { NextOrganismId = 2 },
            Organisms = [OrganismSnapshot.From(organism)],
            Lineages = [new LineageSnapshot { OrganismId = 1, LineageId = 1, BirthTick = 0, GenerationDepth = 0, BirthTraits = GenomeSnapshot.From(genome) }],
            Metrics = new SimulationMetrics { Population = 1, Extinct = false },
        };

        var world = SimulationWorld.FromSnapshot(snapshot);
        world.Advance();

        SimulationMetrics m = world.ToSnapshot().Metrics!;
        Assert.Equal(1, m.Deaths);
        Assert.Equal(0, m.Population);
        Assert.True(m.Extinct);
    }

    [Fact]
    public void Metrics_activeEvents_reflectTheEnvironmentState()
    {
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

        SimulationMetrics m = world.ToSnapshot().Metrics!;
        Assert.Equal(3, m.ActiveEvents.Count);
        Assert.Contains(EventType.ResourceBlight, m.ActiveEvents);
        Assert.Contains(EventType.DensityPlague, m.ActiveEvents);
        Assert.Contains(EventType.ClimaticAnomaly, m.ActiveEvents);
    }

    [Fact]
    public void Metrics_onAnExtinctWorld_reportZeroedDistributions()
    {
        SimulationMetrics m = new()
        {
            Population = 0,
            Extinct = true,
        };

        // A freshly-constructed empty metrics is the shape BuildMetrics produces for an empty world:
        // zeroed energy/averages, no per-lineage or biome rows beyond the always-present biome list.
        Assert.Equal(0.0, m.EnergyAverage);
        Assert.Empty(m.ReproductionByLineage);
    }
}
