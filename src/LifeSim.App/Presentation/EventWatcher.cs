using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.App.Presentation;

/// <summary>Severity of an in-app notification, mapped to a Fluent toast style by the view.</summary>
public enum SimNotificationKind
{
    Info,
    Warning,
    Success,
}

/// <summary>
/// A single in-app notification: a short title + detail and a severity. When it
/// concerns one organism (a multicellular milestone, a sterile-soma body), <see cref="OrganismId"/>
/// carries that organism so clicking the toast can select it on the map, exactly like a map click.
/// </summary>
public sealed record SimNotification(string Title, string Detail, SimNotificationKind Kind, long? OrganismId = null);

/// <summary>
/// Watches the snapshot stream and flags notable transitions as in-app notifications. Pure and UI-only: everything is derived from consecutive <see cref="WorldSnapshot"/> frames,
/// never from the engine. The first frame seeds baselines silently (so a loaded mid-run world doesn't
/// announce its whole history), a tick regression resets state (a new/reloaded world), one-off
/// milestones latch, crises re-arm after they clear, and noisy per-tick signals use a cooldown.
/// </summary>
public sealed class EventWatcher
{
    // --- Tunables (UI cadence, not simulation constants). ---
    private const int MinExplosionPopulation = 25;
    private const double ExplosionMultiple = 2.0;
    private const int MinCrashPopulation = 25;
    private const double CrashFraction = 0.5;
    private const int NearExtinctionThreshold = 5;
    private const int NearExtinctionRecovery = 15;
    private const double PopulationRecordFactor = 1.25;
    private const int MinRecordPopulation = 25;
    private const int GenerationMilestoneStep = 10;
    private const long BabyBoomBirths = 15;
    private const long MassDieOffDeaths = 15;
    private const int BurstCooldownTicks = 15;
    private const double StarvationEnergy = 15.0;
    private const double StarvationRecovery = 30.0;
    private const double RegimeMargin = 1.25;
    private const long MinRegimeActivity = 10;
    private const int RegimeCooldownTicks = 40;

    private enum Regime
    {
        None,
        Predator,
        Herbivore,
    }

    private bool _initialized;
    private long _lastTick;
    private readonly HashSet<EventType> _activeEvents = [];
    private int _referencePopulation;   // running trough, for explosions
    private int _crashPeak;             // running peak, for crashes
    private int _populationRecord;      // all-time high announced
    private int _cellMilestone = 1;
    private int _generationMilestone;
    private bool _extinctionAnnounced;
    private bool _nearExtinctionArmed = true;
    private bool _starvationActive;
    private bool _shareAnnounced;
    private bool _kinPredationAnnounced;
    private bool _sterileSomaAnnounced;
    private readonly HashSet<Biome> _colonizedBiomes = [];
    private Regime _regime = Regime.None;
    private long _burstCooldownUntil;
    private long _regimeCooldownUntil;

    /// <summary>Forget all history so the next frame re-seeds baselines silently (call when a new world is adopted).</summary>
    public void Reset()
    {
        _initialized = false;
        _lastTick = 0;
        _activeEvents.Clear();
        _referencePopulation = 0;
        _crashPeak = 0;
        _populationRecord = 0;
        _cellMilestone = 1;
        _generationMilestone = 0;
        _extinctionAnnounced = false;
        _nearExtinctionArmed = true;
        _starvationActive = false;
        _shareAnnounced = false;
        _kinPredationAnnounced = false;
        _sterileSomaAnnounced = false;
        _colonizedBiomes.Clear();
        _regime = Regime.None;
        _burstCooldownUntil = 0;
        _regimeCooldownUntil = 0;
    }

    /// <summary>Fold in the next frame and return any notifications its transitions warrant (empty on the seeding frame).</summary>
    public IReadOnlyList<SimNotification> Observe(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Tick < _lastTick)
        {
            Reset(); // time went backwards → a new or reloaded world
        }

        _lastTick = snapshot.Tick;
        int population = snapshot.Organisms.Count;
        (int maxCells, long maxCellOrganismId) = LargestBody(snapshot);
        SimulationMetrics? metrics = snapshot.Metrics;

        if (!_initialized)
        {
            Seed(snapshot, population, maxCells, metrics);
            return [];
        }

        var results = new List<SimNotification>();
        DetectEnvironmentEvents(snapshot, results);
        DetectPopulation(population, results);
        DetectCellMilestones(maxCells, maxCellOrganismId, results);
        DetectGenerationMilestones(snapshot, results);
        DetectFirsts(snapshot, metrics, results);
        DetectBiomeColonization(metrics, results);
        DetectFlowBursts(snapshot.Tick, metrics, results);
        DetectStarvation(population, metrics, results);
        DetectForagingRegime(snapshot.Tick, metrics, results);
        return results;
    }

    private void Seed(WorldSnapshot snapshot, int population, int maxCells, SimulationMetrics? metrics)
    {
        _initialized = true;
        foreach (EnvironmentModifier modifier in snapshot.EnvironmentModifiers)
        {
            _activeEvents.Add(modifier.Type);
        }

        _referencePopulation = population;
        _crashPeak = population;
        _populationRecord = population;
        _cellMilestone = Math.Max(1, maxCells);
        _generationMilestone = MaxGeneration(snapshot) / GenerationMilestoneStep * GenerationMilestoneStep;
        _extinctionAnnounced = population == 0;
        _nearExtinctionArmed = population > NearExtinctionThreshold;
        _shareAnnounced = metrics is { SuccessfulShare: > 0 };
        _kinPredationAnnounced = metrics is { KinPredation: > 0 };
        _sterileSomaAnnounced = SterileSomaId(snapshot) is not null;
        foreach (Biome biome in HostileBiomes)
        {
            if (BiomeCount(metrics, biome) > 0)
            {
                _colonizedBiomes.Add(biome);
            }
        }

        _regime = CandidateRegime(metrics); // seed the initial foraging regime silently
    }

    private void DetectEnvironmentEvents(WorldSnapshot snapshot, List<SimNotification> results)
    {
        var current = new HashSet<EventType>();
        foreach (EnvironmentModifier modifier in snapshot.EnvironmentModifiers)
        {
            current.Add(modifier.Type);
        }

        foreach (EventType type in Enum.GetValues<EventType>())
        {
            bool active = current.Contains(type);
            bool was = _activeEvents.Contains(type);
            if (active && !was)
            {
                results.Add(EventOnset(type));
                _activeEvents.Add(type);
            }
            else if (!active && was)
            {
                results.Add(EventEnded(type));
                _activeEvents.Remove(type);
            }
        }
    }

    private void DetectPopulation(int population, List<SimNotification> results)
    {
        if (population == 0)
        {
            if (!_extinctionAnnounced)
            {
                results.Add(new SimNotification("Extinction", "The population has died out — the world is empty.", SimNotificationKind.Warning));
                _extinctionAnnounced = true;
            }

            return;
        }

        _extinctionAnnounced = false;

        // Near-extinction: fire once as the population falls to a sliver; re-arm after it recovers.
        if (population <= NearExtinctionThreshold && _nearExtinctionArmed)
        {
            results.Add(new SimNotification("Near extinction", $"Only {population} organisms remain.", SimNotificationKind.Warning));
            _nearExtinctionArmed = false;
        }
        else if (population >= NearExtinctionRecovery)
        {
            _nearExtinctionArmed = true;
        }

        // Crash: population at most half its recent peak.
        _crashPeak = Math.Max(_crashPeak, population);
        if (_crashPeak >= MinCrashPopulation && population <= _crashPeak * CrashFraction)
        {
            results.Add(new SimNotification("Population crash", $"Down to {population} from a recent peak of {_crashPeak}.", SimNotificationKind.Warning));
            _crashPeak = population;
        }

        // Explosion: population at least double its recent trough.
        _referencePopulation = Math.Min(_referencePopulation, population);
        if (_referencePopulation > 0 && population >= MinExplosionPopulation && population >= _referencePopulation * ExplosionMultiple)
        {
            results.Add(new SimNotification("Population explosion", $"{population} organisms — more than double the recent low of {_referencePopulation}.", SimNotificationKind.Info));
            _referencePopulation = population;
        }

        // New all-time-high record (only on a meaningful jump, so it doesn't fire every tick).
        if (population >= MinRecordPopulation && population >= _populationRecord * PopulationRecordFactor)
        {
            results.Add(new SimNotification("Population record", $"A new high of {population} organisms.", SimNotificationKind.Success));
            _populationRecord = population;
        }
    }

    private void DetectCellMilestones(int maxCells, long organismId, List<SimNotification> results)
    {
        for (int cells = _cellMilestone + 1; cells <= maxCells; cells++)
        {
            results.Add(new SimNotification(
                $"First {cells}-cell organism",
                $"A multicellular body of {cells} cells has evolved. Click to inspect it.",
                SimNotificationKind.Success,
                organismId));
        }

        if (maxCells > _cellMilestone)
        {
            _cellMilestone = maxCells;
        }
    }

    private void DetectGenerationMilestones(WorldSnapshot snapshot, List<SimNotification> results)
    {
        int maxGeneration = MaxGeneration(snapshot);
        while (_generationMilestone + GenerationMilestoneStep <= maxGeneration)
        {
            _generationMilestone += GenerationMilestoneStep;
            results.Add(new SimNotification($"Generation {_generationMilestone}", $"A lineage has reached generation {_generationMilestone}.", SimNotificationKind.Success));
        }
    }

    private void DetectFirsts(WorldSnapshot snapshot, SimulationMetrics? metrics, List<SimNotification> results)
    {
        if (!_shareAnnounced && metrics is { SuccessfulShare: > 0 })
        {
            results.Add(new SimNotification("First cooperation", "An organism has shared energy with a neighbour.", SimNotificationKind.Success));
            _shareAnnounced = true;
        }

        if (!_kinPredationAnnounced && metrics is { KinPredation: > 0 })
        {
            results.Add(new SimNotification("First kin predation", "An organism has cannibalised a close relative.", SimNotificationKind.Info));
            _kinPredationAnnounced = true;
        }

        if (!_sterileSomaAnnounced && SterileSomaId(snapshot) is { } somaId)
        {
            results.Add(new SimNotification(
                "First sterile soma",
                "A body has evolved too few germ cells to reproduce. Click to inspect it.",
                SimNotificationKind.Info,
                somaId));
            _sterileSomaAnnounced = true;
        }
    }

    private void DetectBiomeColonization(SimulationMetrics? metrics, List<SimNotification> results)
    {
        foreach (Biome biome in HostileBiomes)
        {
            if (BiomeCount(metrics, biome) > 0 && _colonizedBiomes.Add(biome))
            {
                results.Add(new SimNotification($"Colonised the {BiomeName(biome)}", $"An organism is surviving in the {BiomeName(biome)}.", SimNotificationKind.Success));
            }
        }
    }

    private void DetectFlowBursts(long tick, SimulationMetrics? metrics, List<SimNotification> results)
    {
        if (metrics is null || tick < _burstCooldownUntil)
        {
            return;
        }

        if (metrics.Births >= BabyBoomBirths)
        {
            results.Add(new SimNotification("Baby boom", $"{metrics.Births} births in a single tick.", SimNotificationKind.Info));
            _burstCooldownUntil = tick + BurstCooldownTicks;
        }
        else if (metrics.Deaths >= MassDieOffDeaths)
        {
            results.Add(new SimNotification("Mass die-off", $"{metrics.Deaths} deaths in a single tick.", SimNotificationKind.Warning));
            _burstCooldownUntil = tick + BurstCooldownTicks;
        }
    }

    private void DetectStarvation(int population, SimulationMetrics? metrics, List<SimNotification> results)
    {
        if (metrics is null || population == 0)
        {
            return;
        }

        if (!_starvationActive && metrics.EnergyAverage < StarvationEnergy)
        {
            results.Add(new SimNotification("Widespread starvation", $"Average energy has fallen to {metrics.EnergyAverage:F0}.", SimNotificationKind.Warning));
            _starvationActive = true;
        }
        else if (metrics.EnergyAverage >= StarvationRecovery)
        {
            _starvationActive = false;
        }
    }

    private void DetectForagingRegime(long tick, SimulationMetrics? metrics, List<SimNotification> results)
    {
        Regime candidate = CandidateRegime(metrics);
        if (candidate == Regime.None || candidate == _regime || tick < _regimeCooldownUntil)
        {
            return; // too little foraging to tell, no shift, or still cooling down
        }

        results.Add(candidate == Regime.Predator
            ? new SimNotification("Predatory era", "Hunting has overtaken grazing as the main food source.", SimNotificationKind.Info)
            : new SimNotification("Herbivore era", "Grazing has overtaken hunting as the main food source.", SimNotificationKind.Info));
        _regime = candidate;
        _regimeCooldownUntil = tick + RegimeCooldownTicks;
    }

    /// <summary>The clearly-dominant foraging strategy this tick, or None when foraging is scarce or balanced.</summary>
    private static Regime CandidateRegime(SimulationMetrics? metrics)
    {
        if (metrics is null || metrics.SuccessfulPredation + metrics.SuccessfulGrazing < MinRegimeActivity)
        {
            return Regime.None;
        }

        if (metrics.SuccessfulPredation > metrics.SuccessfulGrazing * RegimeMargin)
        {
            return Regime.Predator;
        }

        return metrics.SuccessfulGrazing > metrics.SuccessfulPredation * RegimeMargin ? Regime.Herbivore : Regime.None;
    }

    private static int MaxGeneration(WorldSnapshot snapshot)
    {
        int max = 0;
        foreach (LineageSnapshot lineage in snapshot.Lineages)
        {
            max = Math.Max(max, lineage.GenerationDepth);
        }

        return max;
    }

    /// <summary>The id of the first sterile-soma body (too few germ cells to reproduce), or null when none / multicellularity is off.</summary>
    private static long? SterileSomaId(WorldSnapshot snapshot)
    {
        MulticellularConfig config = snapshot.Configuration.Multicellular;
        if (!config.Enabled)
        {
            return null;
        }

        foreach (OrganismSnapshot organism in snapshot.Organisms)
        {
            if (!Morphology.CanReproduce(organism.Genome.ToGenome(), config))
            {
                return organism.OrganismId;
            }
        }

        return null;
    }

    /// <summary>The whole-cell count of the largest body and the id of the organism that carries it (id -1 if the world is empty).</summary>
    private static (int Cells, long OrganismId) LargestBody(WorldSnapshot snapshot)
    {
        MulticellularConfig config = snapshot.Configuration.Multicellular;
        double best = 0.0;
        long id = -1;
        foreach (OrganismSnapshot organism in snapshot.Organisms)
        {
            double cells = Morphology.CellCount(organism.Genome.ToGenome(), config);
            if (cells > best)
            {
                best = cells;
                id = organism.OrganismId;
            }
        }

        return ((int)Math.Floor(Math.Max(1.0, best)), id);
    }

    private static readonly Biome[] HostileBiomes = [Biome.Desert, Biome.Swamp, Biome.IceSheet];

    private static long BiomeCount(SimulationMetrics? metrics, Biome biome)
    {
        if (metrics is null)
        {
            return 0;
        }

        foreach (BiomePopulation entry in metrics.PopulationByBiome)
        {
            if (entry.Biome == biome)
            {
                return entry.Count;
            }
        }

        return 0;
    }

    private static string BiomeName(Biome biome) => biome switch
    {
        Biome.IceSheet => "Ice Sheet",
        _ => biome.ToString(),
    };

    private static SimNotification EventOnset(EventType type) => type switch
    {
        EventType.ResourceBlight => new SimNotification("Resource blight", "Ground energy regeneration has halted across a biome.", SimNotificationKind.Warning),
        EventType.DensityPlague => new SimNotification("Density plague", "Crowded regions are being drained of energy.", SimNotificationKind.Warning),
        EventType.ClimaticAnomaly => new SimNotification("Climatic anomaly", "A heatwave or ice age has shifted temperatures.", SimNotificationKind.Info),
        _ => new SimNotification("Environmental event", type.ToString(), SimNotificationKind.Info),
    };

    private static SimNotification EventEnded(EventType type) => type switch
    {
        EventType.ResourceBlight => new SimNotification("Blight lifted", "Ground energy regeneration has resumed.", SimNotificationKind.Info),
        EventType.DensityPlague => new SimNotification("Plague passed", "The density plague has run its course.", SimNotificationKind.Info),
        EventType.ClimaticAnomaly => new SimNotification("Climate settled", "Temperatures have returned to normal.", SimNotificationKind.Info),
        _ => new SimNotification("Event ended", type.ToString(), SimNotificationKind.Info),
    };
}
