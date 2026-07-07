using LifeSim.App.Presentation;
using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Tests;

public class EventWatcherTests
{
    private static WorldSnapshot Frame(long tick, int population, double maxCell = 1.0, bool multicellular = true, params EventType[] events)
    {
        var organisms = new List<OrganismSnapshot>(population);
        for (int i = 0; i < population; i++)
        {
            // One organism carries the body's max cell count; the rest are single cells.
            organisms.Add(new OrganismSnapshot { Genome = new GenomeSnapshot { CellCount = i == 0 ? maxCell : 1.0 } });
        }

        return new WorldSnapshot
        {
            Tick = tick,
            Configuration = SimulationConfig.Default with
            {
                Multicellular = SimulationConfig.Default.Multicellular with { Enabled = multicellular },
            },
            Organisms = organisms,
            EnvironmentModifiers = [.. events.Select(t => new EnvironmentModifier { Type = t, RemainingTicks = 10 })],
        };
    }

    [Fact]
    public void FirstFrame_seedsBaselinesSilently()
    {
        var watcher = new EventWatcher();
        Assert.Empty(watcher.Observe(Frame(tick: 0, population: 40, events: EventType.ResourceBlight)));
    }

    [Fact]
    public void NewEnvironmentEvent_notifiesOnce_thenReArmsAfterItEnds()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40));                                              // seed, no events

        IReadOnlyList<SimNotification> onset = watcher.Observe(Frame(1, 40, events: EventType.ResourceBlight));
        Assert.Single(onset);
        Assert.Equal("Resource blight", onset[0].Title);
        Assert.Equal(SimNotificationKind.Warning, onset[0].Kind);

        Assert.Empty(watcher.Observe(Frame(2, 40, events: EventType.ResourceBlight))); // still active → silent
        Assert.Empty(watcher.Observe(Frame(3, 40)));                                    // ended → silent

        Assert.Single(watcher.Observe(Frame(4, 40, events: EventType.ResourceBlight))); // recurs → notifies again
    }

    [Fact]
    public void SimultaneousEvents_eachNotifyInEnumOrder()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40));

        IReadOnlyList<SimNotification> events = watcher.Observe(
            Frame(1, 40, events: [EventType.DensityPlague, EventType.ResourceBlight]));

        Assert.Equal(["Resource blight", "Density plague"], events.Select(n => n.Title)); // declared enum order
    }

    [Fact]
    public void PopulationExplosion_firesWhenDoublingFromARecentTrough()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 100));            // seed high
        Assert.Empty(watcher.Observe(Frame(1, 20))); // crash → trough, no explosion

        IReadOnlyList<SimNotification> boom = watcher.Observe(Frame(2, 40)); // 40 >= 2 x 20
        Assert.Single(boom);
        Assert.Equal("Population explosion", boom[0].Title);

        Assert.Empty(watcher.Observe(Frame(3, 45))); // not another doubling from the new high
    }

    [Fact]
    public void PopulationExplosion_ignoresTinyPopulations()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 5));
        watcher.Observe(Frame(1, 2));
        Assert.Empty(watcher.Observe(Frame(2, 10))); // doubled, but below the meaningful-size floor
    }

    [Fact]
    public void FirstMulticellularBody_notifiesPerNewCellMilestone()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40, maxCell: 1.0));

        IReadOnlyList<SimNotification> two = watcher.Observe(Frame(1, 40, maxCell: 2.3));
        Assert.Single(two);
        Assert.Equal("First 2-cell organism", two[0].Title);
        Assert.Equal(SimNotificationKind.Success, two[0].Kind);

        Assert.Empty(watcher.Observe(Frame(2, 40, maxCell: 2.9))); // still a 2-cell body → silent

        IReadOnlyList<SimNotification> jump = watcher.Observe(Frame(3, 40, maxCell: 4.1));
        Assert.Equal(["First 3-cell organism", "First 4-cell organism"], jump.Select(n => n.Title));
    }

    [Fact]
    public void CellMilestones_areSuppressedWhenMulticellularityIsDisabled()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40, maxCell: 1.0, multicellular: false));
        Assert.Empty(watcher.Observe(Frame(1, 40, maxCell: 8.0, multicellular: false)));
    }

    [Fact]
    public void TickRegression_resetsAndReSeedsSilently()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(100, 40));
        watcher.Observe(Frame(101, 40, events: EventType.DensityPlague)); // notified

        // A brand-new world (tick back to 0) with a plague already active must re-seed, not notify.
        Assert.Empty(watcher.Observe(Frame(0, 40, events: EventType.DensityPlague)));
    }

    [Fact]
    public void Reset_forgetsHistory()
    {
        var watcher = new EventWatcher();
        watcher.Observe(Frame(0, 40, events: EventType.ResourceBlight));
        watcher.Reset();
        Assert.Empty(watcher.Observe(Frame(1, 40, events: EventType.ResourceBlight))); // treated as a fresh seed
    }
}
