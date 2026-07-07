using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Calibration.Tests;

/// <summary>
/// Fixed-seed calibration scenarios (lifesim.md §15) run through the <c>sim</c> CLI harness. These
/// don't prove evolution is "correct"; they catch obvious runaway dynamics and broken mechanics
/// with the tuned default configuration.
/// </summary>
public class BaselineScenarioTests
{
    // 1. Grassland Survival — a small population in stable grassland survives without extinction and
    //    without runaway growth.
    [Fact]
    public void GrasslandSurvival_populationPersistsWithoutExtinctionOrExplosion()
    {
        using var harness = new ScenarioHarness();
        WorldSnapshot genesis = harness.Genesis(seed: 42, width: 64, height: 64, population: 40);

        RunResult result = harness.Run(genesis, ticks: 150);

        Assert.All(result.Populations, p => Assert.True(p > 0, "Population went extinct mid-run."));
        Assert.True(result.Populations[^1] >= 5, $"Final population {result.Populations[^1]} is unhealthily low.");
        Assert.True(result.Populations.Max() <= 200, $"Population exploded to {result.Populations.Max()} (runaway growth).");
    }

    // 2. Desert Stress — a heat-intolerant genome pays measurably more metabolic energy in desert
    //    than in grassland over a single tick (all else held equal, no grazing/movement).
    [Fact]
    public void DesertStress_heatIntolerantOrganismLosesMoreEnergyInDesertThanGrassland()
    {
        using var harness = new ScenarioHarness();
        var config = SimulationConfig.Default;
        var world = new WorldState { Seed = 1, Width = 96, Height = 96 };
        var terrain = new TerrainSampler(world.Seed, config);

        (int X, int Y) desert = ScenarioHarness.FindTile(terrain, Biome.Desert);
        (int X, int Y) grass = ScenarioHarness.FindTile(terrain, Biome.Grassland);

        // Envelope [4.5, 25.5]°C covers grassland (~20°C) but not desert (~45°C).
        Genome genome = ScenarioHarness.InertGenome(thermalCenter: 15.0, thermalWidth: 21.0);
        Organism inDesert = ScenarioHarness.MakeOrganism(1, genome, energy: 60.0, desert.X, desert.Y);
        Organism inGrass = ScenarioHarness.MakeOrganism(2, genome, energy: 60.0, grass.X, grass.Y);

        List<GroundEnergyEntry> drained =
        [
            .. ScenarioHarness.DrainedCross(desert.X, desert.Y),
            .. ScenarioHarness.DrainedCross(grass.X, grass.Y),
        ];
        WorldSnapshot snapshot = ScenarioHarness.BuildWorld(world, config, [inDesert, inGrass], drained);

        RunResult result = harness.Run(snapshot, ticks: 1);
        double desertEnergy = result.Final.Organisms.Single(o => o.OrganismId == 1).Energy;
        double grassEnergy = result.Final.Organisms.Single(o => o.OrganismId == 2).Energy;

        Assert.True(desertEnergy < grassEnergy,
            $"Desert energy {desertEnergy} should be below grassland {grassEnergy} (thermal stress).");
    }

    // 3. Swamp Movement Cost — swamp is energy-rich yet costlier to move through than grassland.
    [Fact]
    public void SwampMovementCost_isEnergyRichButCostlierToTraverseThanGrassland()
    {
        BiomesConfig biomes = SimulationConfig.Default.Biomes;
        MovementCombatConfig movement = SimulationConfig.Default.MovementCombat;

        // Abundant energy: higher cap and faster regen than grassland.
        Assert.True(biomes.Swamp.EnergyCap > biomes.Grassland.EnergyCap);
        Assert.True(biomes.Swamp.RegenRate > biomes.Grassland.RegenRate);

        // Visibly higher movement cost: identical travel costs more under swamp friction.
        double swampMove = Metabolism.LocomotionTax(distance: 1.0, velocity: 2.5, biomes.Swamp.Friction, movement);
        double grassMove = Metabolism.LocomotionTax(distance: 1.0, velocity: 2.5, biomes.Grassland.Friction, movement);
        Assert.True(swampMove > grassMove, $"Swamp move {swampMove} should exceed grassland move {grassMove}.");
    }

    // 4. Predator/Prey Transfer — predation is viable for a larger organism but never cost-free, and
    //    a live run actually produces energy-transferring kills.
    [Fact]
    public void PredatorPreyTransfer_isViableButCarriesRealCostAndRisk()
    {
        SimulationConfig config = SimulationConfig.Default;

        // Viable but uncertain: a big attacker usually wins, never with certainty; equal sizes are 50/50.
        double bigVsSmall = Combat.KillProbability(attackerSize: 8.0, victimSize: 1.0);
        Assert.True(bigVsSmall is > 0.5 and < 1.0);
        Assert.Equal(0.5, Combat.KillProbability(1.0, 1.0), precision: 10);

        // Not cost-free: failure is penalised, and being a big predator costs more upkeep and more to reproduce.
        Assert.True(config.MovementCombat.FailedCombatPenalty > 0.0);
        Assert.True(config.MovementCombat.PredationTransferFraction is > 0.0 and < 1.0);
        Assert.True(Metabolism.BaseMetabolism(new Genome { Size = 8.0 }, config.Metabolism)
            > Metabolism.BaseMetabolism(new Genome { Size = 1.0 }, config.Metabolism));

        // Live demonstration: a large predator ringed by small prey lands energy-transferring kills.
        using var harness = new ScenarioHarness();
        var world = new WorldState { Seed = 7, Width = 32, Height = 32 };
        Genome predatorGenome = ScenarioHarness.InertGenome(thermalWidth: 1000.0) with { Size = 8.0 };
        Genome preyGenome = ScenarioHarness.InertGenome(thermalWidth: 1000.0) with { Size = 1.0 };

        var organisms = new List<Organism> { ScenarioHarness.MakeOrganism(1, predatorGenome, energy: 50.0, 10, 10) };
        long id = 2;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx != 0 || dy != 0)
                {
                    organisms.Add(ScenarioHarness.MakeOrganism(id++, preyGenome, energy: 30.0, 10 + dx, 10 + dy));
                }
            }
        }

        WorldSnapshot snapshot = ScenarioHarness.BuildWorld(world, config, organisms);
        RunResult result = harness.Run(snapshot, ticks: 30);

        Assert.True(result.Total("successful_predation") > 0, "Expected at least one successful predation over the run.");
    }

    // 5. Overcrowding Plague — during a plague, crowded organisms are drained while isolated ones aren't.
    [Fact]
    public void OvercrowdingPlague_penalizesCrowdedOrganismsAndSparesIsolatedOnes()
    {
        using var harness = new ScenarioHarness();
        var config = SimulationConfig.Default with
        {
            Events = new EventsConfig() with
            {
                BlightProbability = 0.0,
                PlagueProbability = 1.0,
                TemperatureAnomalyProbability = 0.0,
                PlagueDensityThreshold = 2,
                PlagueEnergyDrainPerTick = 3.0,
                PlagueDurationTicks = 10,
            },
        };
        var world = new WorldState { Seed = 555, Width = 64, Height = 64 };
        Genome genome = ScenarioHarness.InertGenome(thermalWidth: 1000.0); // thermal-neutral everywhere → isolates the plague drain

        // Diagonal pair each see the other in their 3×3 window (density 2) but can't interact
        // (Move/Harvest are orthogonal-only); the third organism is isolated (density 1).
        Organism a = ScenarioHarness.MakeOrganism(1, genome, energy: 40.0, 10, 10);
        Organism b = ScenarioHarness.MakeOrganism(2, genome, energy: 40.0, 11, 11);
        Organism isolated = ScenarioHarness.MakeOrganism(3, genome, energy: 40.0, 40, 40);

        List<GroundEnergyEntry> drained =
        [
            .. ScenarioHarness.DrainedCross(10, 10),
            .. ScenarioHarness.DrainedCross(11, 11),
            .. ScenarioHarness.DrainedCross(40, 40),
        ];
        WorldSnapshot snapshot = ScenarioHarness.BuildWorld(world, config, [a, b, isolated], drained);

        RunResult result = harness.Run(snapshot, ticks: 1);
        double crowded = result.Final.Organisms.Single(o => o.OrganismId == 1).Energy;
        double lonely = result.Final.Organisms.Single(o => o.OrganismId == 3).Energy;

        Assert.True(crowded < lonely, $"Crowded organism {crowded} should be drained below the isolated one {lonely}.");
    }

    // 6. Blight Recovery — a buffered population survives a resource collapse that starves a
    //    reserve-less ("greedy") one, and the collapse is temporary (the blight expires).
    [Fact]
    public void BlightRecovery_bufferedOrganismOutlastsAReserveLessOne_andTheCollapseEnds()
    {
        using var harness = new ScenarioHarness();
        var config = SimulationConfig.Default with
        {
            Events = new EventsConfig() with { BlightProbability = 0.0, PlagueProbability = 0.0, TemperatureAnomalyProbability = 0.0 },
        };
        var world = new WorldState { Seed = 555, Width = 64, Height = 64 };
        Genome genome = ScenarioHarness.InertGenome(thermalWidth: 1000.0);

        Organism buffered = ScenarioHarness.MakeOrganism(1, genome, energy: 80.0, 5, 5);
        Organism greedy = ScenarioHarness.MakeOrganism(2, genome, energy: 2.0, 20, 20);

        // A resource collapse: every tile drained and regen halted by a pre-loaded blight window.
        List<GroundEnergyEntry> drained = [.. ScenarioHarness.DrainedCross(5, 5), .. ScenarioHarness.DrainedCross(20, 20)];
        List<EnvironmentModifier> blight = [new EnvironmentModifier { Type = EventType.ResourceBlight, StartTick = 0, RemainingTicks = 15, Magnitude = 0.0 }];
        WorldSnapshot snapshot = ScenarioHarness.BuildWorld(world, config, [buffered, greedy], drained, blight);

        RunResult result = harness.Run(snapshot, ticks: 25);

        // Founders found their own lineage (lineage_id == organism_id); offspring inherit it. The
        // buffered founder's reserve lets its lineage ride out the collapse and reproduce; the
        // reserve-less founder starves before it can, so its lineage dies out entirely.
        Dictionary<long, long> lineageOf = result.Final.Lineages.ToDictionary(l => l.OrganismId, l => l.LineageId);
        HashSet<long> livingLineages = result.Final.Organisms.Select(o => lineageOf[o.OrganismId]).ToHashSet();

        Assert.Contains(1L, livingLineages);        // buffered lineage recovered
        Assert.DoesNotContain(2L, livingLineages);  // reserve-less lineage died out
        Assert.Empty(result.Final.EnvironmentModifiers); // the collapse was temporary — the blight expired
    }
}
