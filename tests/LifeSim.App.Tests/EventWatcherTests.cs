using LifeSim.App.Presentation;
using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.App.Tests;

public class EventWatcherTests
{
    private static WorldSnapshot Frame(
        long tick,
        int population,
        double maxCell = 1.0,
        bool multicellular = true,
        EventType[]? events = null,
        SimulationMetrics? metrics = null,
        int maxGeneration = 0,
        bool sterileSoma = false)
    {
        var organisms = new List<OrganismSnapshot>(population);
        for (int i = 0; i < population; i++)
        {
            var genome = new GenomeSnapshot { CellCount = i == 0 ? maxCell : 1.0 };
            if (sterileSoma && i == 0)
            {
                // All Feeder, no Germ → germ fraction 0 → sterile soma (needs a multicellular body).
                genome = genome with { CellCount = Math.Max(2.0, maxCell), FeederWeight = 1.0 };
            }

            organisms.Add(new OrganismSnapshot { OrganismId = i + 1, Genome = genome });
        }

        return new WorldSnapshot
        {
            Tick = tick,
            Configuration = SimulationConfig.Default with
            {
                Multicellular = SimulationConfig.Default.Multicellular with { Enabled = multicellular },
            },
            Organisms = organisms,
            Lineages = maxGeneration > 0 ? [new LineageSnapshot { OrganismId = 1, GenerationDepth = maxGeneration }] : [],
            EnvironmentModifiers = events is null ? [] : [.. events.Select(t => new EnvironmentModifier { Type = t, RemainingTicks = 10 })],
            Metrics = metrics,
        };
    }

    private static SimulationMetrics Metrics(
        long births = 0,
        long deaths = 0,
        long predation = 0,
        long grazing = 0,
        long share = 0,
        long kinPredation = 0,
        double energyAvg = 50.0,
        (Biome Biome, long Count)[]? biomes = null)
        => new()
        {
            Births = births,
            Deaths = deaths,
            SuccessfulPredation = predation,
            SuccessfulGrazing = grazing,
            SuccessfulShare = share,
            KinPredation = kinPredation,
            EnergyAverage = energyAvg,
            PopulationByBiome = biomes is null ? [] : [.. biomes.Select(b => new BiomePopulation { Biome = b.Biome, Count = b.Count })],
        };

    private static IReadOnlyList<string> Titles(IReadOnlyList<SimNotification> notifications) => [.. notifications.Select(n => n.Title)];

    // --- Seeding & environment events ---

    [Fact]
    public void FirstFrame_seedsBaselinesSilently()
    {
        var watcher = new EventWatcher();
        Assert.Empty(watcher.Observe(Frame(tick: 0, population: 40, events: [EventType.ResourceBlight])));
    }

    [Fact]
    public void EnvironmentEvent_notifiesOnStartAndEnd_thenReArms()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40));

        Assert.Equal(["Resource blight"], Titles(watcher.Observe(Frame(1, 40, events: [EventType.ResourceBlight]))));
        Assert.Empty(watcher.Observe(Frame(2, 40, events: [EventType.ResourceBlight])));       // still active
        Assert.Equal(["Blight lifted"], Titles(watcher.Observe(Frame(3, 40))));                 // ended
        Assert.Equal(["Resource blight"], Titles(watcher.Observe(Frame(4, 40, events: [EventType.ResourceBlight])))); // recurs
    }

    [Fact]
    public void SimultaneousEvents_notifyInEnumOrder()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40));
        Assert.Equal(["Resource blight", "Density plague"],
            Titles(watcher.Observe(Frame(1, 40, events: [EventType.DensityPlague, EventType.ResourceBlight]))));
    }

    // --- Population dynamics ---

    [Fact]
    public void Extinction_firesOnceWhenPopulationHitsZero()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 10));
        Assert.Contains("Extinction", Titles(watcher.Observe(Frame(1, 0))));
        Assert.DoesNotContain("Extinction", Titles(watcher.Observe(Frame(2, 0))));
    }

    [Fact]
    public void NearExtinction_firesOnceThenReArmsAfterRecovery()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 6)); // small peak so a crash isn't also triggered

        Assert.Contains("Near extinction", Titles(watcher.Observe(Frame(1, 3))));
        Assert.DoesNotContain("Near extinction", Titles(watcher.Observe(Frame(2, 4)))); // still low, already armed off
        watcher.Observe(Frame(3, 20));                                                  // recovers → re-arm
        Assert.Contains("Near extinction", Titles(watcher.Observe(Frame(4, 2))));
    }

    [Fact]
    public void PopulationCrash_firesWhenHalvingFromARecentPeak()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 100));
        Assert.Equal(["Population crash"], Titles(watcher.Observe(Frame(1, 40))));
    }

    [Fact]
    public void PopulationExplosion_firesWhenDoublingFromARecentTrough()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 100));
        watcher.Observe(Frame(1, 20));
        Assert.Contains("Population explosion", Titles(watcher.Observe(Frame(2, 40))));
    }

    [Fact]
    public void PopulationRecord_firesOnAMeaningfulNewHigh()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40));
        Assert.Equal(["Population record"], Titles(watcher.Observe(Frame(1, 60))));
        Assert.DoesNotContain("Population record", Titles(watcher.Observe(Frame(2, 65)))); // not a big enough jump
    }

    // --- Evolutionary milestones ---

    [Fact]
    public void CellMilestones_notifyPerNewWholeCellCount_andCarryTheOrganismId()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40, maxCell: 1.0));

        IReadOnlyList<SimNotification> two = watcher.Observe(Frame(1, 40, maxCell: 2.3));
        Assert.Equal(["First 2-cell organism"], Titles(two));
        Assert.Equal(1, two[0].OrganismId); // the largest body is organism i==0 → id 1, so the toast is click-to-select

        Assert.Empty(watcher.Observe(Frame(2, 40, maxCell: 2.9)));
        Assert.Equal(["First 3-cell organism", "First 4-cell organism"], Titles(watcher.Observe(Frame(3, 40, maxCell: 4.1))));
    }

    [Fact]
    public void CellMilestones_areSuppressedWhenMulticellularityIsDisabled()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40, maxCell: 1.0, multicellular: false));
        Assert.Empty(watcher.Observe(Frame(1, 40, maxCell: 8.0, multicellular: false)));
    }

    [Fact]
    public void GenerationMilestones_fireAtEveryTenthGeneration()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40, maxGeneration: 3));
        Assert.Equal(["Generation 10", "Generation 20"], Titles(watcher.Observe(Frame(1, 40, maxGeneration: 23))));
        Assert.Empty(watcher.Observe(Frame(2, 40, maxGeneration: 25)));
    }

    [Fact]
    public void FirstCooperationAndKinPredation_fireOnce()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40, metrics: Metrics()));
        Assert.Contains("First cooperation", Titles(watcher.Observe(Frame(1, 40, metrics: Metrics(share: 2)))));
        Assert.DoesNotContain("First cooperation", Titles(watcher.Observe(Frame(2, 40, metrics: Metrics(share: 5)))));
        Assert.Contains("First kin predation", Titles(watcher.Observe(Frame(3, 40, metrics: Metrics(kinPredation: 1)))));
    }

    [Fact]
    public void FirstSterileSoma_firesOnce()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40));

        SimNotification soma = watcher.Observe(Frame(1, 40, sterileSoma: true)).Single(n => n.Title == "First sterile soma");
        Assert.Equal(1, soma.OrganismId); // click-to-select the sterile body

        Assert.DoesNotContain("First sterile soma", Titles(watcher.Observe(Frame(2, 40, sterileSoma: true))));
    }

    // --- Ecology ---

    [Fact]
    public void BiomeColonisation_firesOncePerHostileBiome()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40, metrics: Metrics(biomes: [(Biome.Grassland, 40)])));
        Assert.Contains("Colonised the Desert",
            Titles(watcher.Observe(Frame(1, 40, metrics: Metrics(biomes: [(Biome.Grassland, 38), (Biome.Desert, 2)])))));
        Assert.DoesNotContain("Colonised the Desert",
            Titles(watcher.Observe(Frame(2, 40, metrics: Metrics(biomes: [(Biome.Desert, 3)])))));
    }

    [Fact]
    public void BabyBoomAndMassDieOff_fireWithCooldown()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40, metrics: Metrics()));
        Assert.Contains("Baby boom", Titles(watcher.Observe(Frame(1, 40, metrics: Metrics(births: 30)))));
        Assert.DoesNotContain("Baby boom", Titles(watcher.Observe(Frame(2, 40, metrics: Metrics(births: 30))))); // cooldown

        var other = new EventWatcher();
        other.Observe(Frame(0, 40, metrics: Metrics()));
        Assert.Contains("Mass die-off", Titles(other.Observe(Frame(1, 40, metrics: Metrics(deaths: 30)))));
    }

    [Fact]
    public void Starvation_firesWhenAverageEnergyCollapses_thenReArms()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40, metrics: Metrics(energyAvg: 50)));
        Assert.Contains("Widespread starvation", Titles(watcher.Observe(Frame(1, 40, metrics: Metrics(energyAvg: 10)))));
        Assert.DoesNotContain("Widespread starvation", Titles(watcher.Observe(Frame(2, 40, metrics: Metrics(energyAvg: 12))))); // still active
        watcher.Observe(Frame(3, 40, metrics: Metrics(energyAvg: 40)));                                                          // recovers
        Assert.Contains("Widespread starvation", Titles(watcher.Observe(Frame(4, 40, metrics: Metrics(energyAvg: 8)))));
    }

    [Fact]
    public void ForagingRegime_notifiesOnAShift_notOnTheInitialRegime()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40, metrics: Metrics(grazing: 100))); // seeds Herbivore silently
        Assert.Equal(["Predatory era"], Titles(watcher.Observe(Frame(1, 40, metrics: Metrics(predation: 100)))));
    }

    // --- Lifecycle ---

    [Fact]
    public void TickRegression_resetsAndReSeedsSilently()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(100, 40));
        watcher.Observe(Frame(101, 40, events: [EventType.DensityPlague]));
        Assert.Empty(watcher.Observe(Frame(0, 40, events: [EventType.DensityPlague]))); // new world → re-seed
    }
}
