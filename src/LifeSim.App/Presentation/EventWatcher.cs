using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Presentation;

/// <summary>Severity of an in-app notification, mapped to a Fluent toast style by the view.</summary>
public enum SimNotificationKind
{
    Info,
    Warning,
    Success,
}

/// <summary>A single in-app notification (lifesim.md §18): a short title + detail and a severity.</summary>
public sealed record SimNotification(string Title, string Detail, SimNotificationKind Kind);

/// <summary>
/// Watches the snapshot stream and flags notable transitions as in-app notifications (lifesim.md
/// §18) — a new environmental event, a population explosion, or the first time a body of N cells
/// evolves (§21). Pure and UI-only: it derives everything from consecutive <see cref="WorldSnapshot"/>
/// frames and never touches the engine. The first frame seeds baselines silently; a tick regression
/// (a fresh or reloaded world) resets state automatically.
/// </summary>
public sealed class EventWatcher
{
    /// <summary>Below this population an increase is too small to call an "explosion".</summary>
    private const int MinExplosionPopulation = 25;

    /// <summary>A population reaching this multiple of its recent trough counts as an explosion.</summary>
    private const double ExplosionMultiple = 2.0;

    private bool _initialized;
    private long _lastTick;
    private readonly HashSet<EventType> _activeEvents = [];
    private int _referencePopulation;
    private int _cellMilestone = 1;

    /// <summary>Forget all history so the next frame re-seeds baselines silently (call when a new world is adopted).</summary>
    public void Reset()
    {
        _initialized = false;
        _lastTick = 0;
        _activeEvents.Clear();
        _referencePopulation = 0;
        _cellMilestone = 1;
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
        int maxCells = MaxWholeCells(snapshot);

        if (!_initialized)
        {
            _initialized = true;
            SeedActiveEvents(snapshot);
            _referencePopulation = population;
            _cellMilestone = Math.Max(1, maxCells);
            return [];
        }

        var results = new List<SimNotification>();
        DetectEnvironmentEvents(snapshot, results);
        DetectPopulationExplosion(population, results);
        DetectCellMilestones(maxCells, results);
        return results;
    }

    private void DetectEnvironmentEvents(WorldSnapshot snapshot, List<SimNotification> results)
    {
        var current = new HashSet<EventType>();
        foreach (EnvironmentModifier modifier in snapshot.EnvironmentModifiers)
        {
            current.Add(modifier.Type);
        }

        // Iterate the enum in declared order so notification order is stable.
        foreach (EventType type in Enum.GetValues<EventType>())
        {
            if (current.Contains(type) && _activeEvents.Add(type))
            {
                results.Add(EnvironmentNotification(type));
            }
        }

        _activeEvents.RemoveWhere(t => !current.Contains(t)); // let a re-occurrence notify again later
    }

    private void DetectPopulationExplosion(int population, List<SimNotification> results)
    {
        _referencePopulation = Math.Min(_referencePopulation, population); // track the recent trough

        if (_referencePopulation > 0 && population >= MinExplosionPopulation
            && population >= _referencePopulation * ExplosionMultiple)
        {
            results.Add(new SimNotification(
                "Population explosion",
                $"{population} organisms — more than double the recent low of {_referencePopulation}.",
                SimNotificationKind.Info));
            _referencePopulation = population; // re-arm from the new high
        }
    }

    private void DetectCellMilestones(int maxCells, List<SimNotification> results)
    {
        for (int cells = _cellMilestone + 1; cells <= maxCells; cells++)
        {
            results.Add(new SimNotification(
                $"First {cells}-cell organism",
                $"A multicellular body of {cells} cells has evolved.",
                SimNotificationKind.Success));
        }

        if (maxCells > _cellMilestone)
        {
            _cellMilestone = maxCells;
        }
    }

    private void SeedActiveEvents(WorldSnapshot snapshot)
    {
        _activeEvents.Clear();
        foreach (EnvironmentModifier modifier in snapshot.EnvironmentModifiers)
        {
            _activeEvents.Add(modifier.Type);
        }
    }

    private static int MaxWholeCells(WorldSnapshot snapshot)
    {
        MulticellularConfig config = snapshot.Configuration.Multicellular;
        double max = 1.0;
        foreach (OrganismSnapshot organism in snapshot.Organisms)
        {
            max = Math.Max(max, Morphology.CellCount(organism.Genome.ToGenome(), config));
        }

        return (int)Math.Floor(max);
    }

    private static SimNotification EnvironmentNotification(EventType type) => type switch
    {
        EventType.ResourceBlight => new SimNotification(
            "Resource blight", "Ground energy regeneration has halted across a biome.", SimNotificationKind.Warning),
        EventType.DensityPlague => new SimNotification(
            "Density plague", "Crowded regions are being drained of energy.", SimNotificationKind.Warning),
        EventType.ClimaticAnomaly => new SimNotification(
            "Climatic anomaly", "A heatwave or ice age has shifted temperatures.", SimNotificationKind.Info),
        _ => new SimNotification("Environmental event", type.ToString(), SimNotificationKind.Info),
    };
}
