using LifeSim.Core.Metrics;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;

namespace LifeSim.Console.Serve;

/// <summary>
/// Thread-safe access to a running world for the serve endpoint (lifesim.md §1): the background
/// engine loop advances it while HTTP/WebSocket handlers read the latest snapshot or post an edited
/// one back. All access is serialized under a single lock, so the engine and the transport never
/// touch the world concurrently. This class is transport-agnostic and directly unit-testable.
/// </summary>
public sealed class SnapshotService
{
    private readonly Lock _gate = new();
    private SimulationWorld _world;

    public SnapshotService(SimulationWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    public long Tick
    {
        get
        {
            lock (_gate)
            {
                return _world.Tick;
            }
        }
    }

    public bool Extinct
    {
        get
        {
            lock (_gate)
            {
                return _world.Extinct;
            }
        }
    }

    /// <summary>The current world state as a snapshot JSON document (lifesim.md §12).</summary>
    public string CurrentSnapshotJson()
    {
        lock (_gate)
        {
            return SnapshotSerializer.Save(_world.ToSnapshot());
        }
    }

    /// <summary>The current tick's metrics as a single NDJSON line (lifesim.md §14).</summary>
    public string CurrentMetricsLine()
    {
        lock (_gate)
        {
            return MetricsExporter.NdjsonLine(_world.Tick, _world.Metrics);
        }
    }

    /// <summary>Advances one tick; returns false (without advancing) once the world is extinct (lifesim.md §17).</summary>
    public bool AdvanceOnce()
    {
        lock (_gate)
        {
            if (_world.Extinct)
            {
                return false;
            }

            _world.Advance();
            return true;
        }
    }

    /// <summary>
    /// Replaces the running world with an imported (edited) snapshot — the browser demo's write-back
    /// path (lifesim.md §1, §16). The snapshot is validated by <see cref="SnapshotSerializer.Load"/>
    /// before it is adopted, so a malformed post never corrupts the live world.
    /// </summary>
    public void Import(string snapshotJson)
    {
        WorldSnapshot snapshot = SnapshotSerializer.Load(snapshotJson);
        SimulationWorld imported = SimulationWorld.FromSnapshot(snapshot);
        lock (_gate)
        {
            _world = imported;
        }
    }
}
