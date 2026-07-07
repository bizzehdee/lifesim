using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Engine;

/// <summary>
/// Drives a live <see cref="SimulationWorld"/> on a background thread (lifesim.md §1): the UI thread
/// never blocks on ticks. Supports play / pause / frame-step / speed, and pushes a fresh snapshot to
/// the <c>onFrame</c> sink after every advance. The sink is responsible for marshalling onto the UI
/// thread (the desktop head wraps it in the Avalonia dispatcher). The Core stays authoritative — this
/// only calls <see cref="SimulationWorld.Advance"/> and <see cref="SimulationWorld.ToSnapshot"/>.
/// </summary>
public sealed class EngineRunner : IDisposable
{
    private readonly Action<WorldSnapshot> _onFrame;
    private readonly Lock _gate = new();
    private readonly SimulationWorld _world;

    private Thread? _thread;
    private volatile bool _playing;
    private volatile bool _stopping;
    private volatile int _pendingSteps;
    private volatile int _delayMs = 100; // 10 ticks/s default

    public EngineRunner(SimulationWorld world, Action<WorldSnapshot> onFrame)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(onFrame);
        _world = world;
        _onFrame = onFrame;
        _onFrame(CaptureSnapshot()); // show the initial frame immediately
    }

    public bool IsPlaying => _playing;

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

    /// <summary>Starts the background loop (idempotent) and begins advancing.</summary>
    public void Play()
    {
        EnsureThread();
        _playing = true;
    }

    public void Pause() => _playing = false;

    /// <summary>Requests exactly one tick while paused (queued, applied by the loop).</summary>
    public void Step()
    {
        EnsureThread();
        Interlocked.Increment(ref _pendingSteps);
    }

    public void SetTicksPerSecond(double ticksPerSecond)
    {
        double clamped = Math.Clamp(ticksPerSecond, 0.5, 1000.0);
        _delayMs = Math.Max(1, (int)Math.Round(1000.0 / clamped));
    }

    /// <summary>Live-adjust the brain-evaluation thread count (execution-only; results are unaffected, lifesim.md §7).</summary>
    public void SetMaxDegreeOfParallelism(int threads)
    {
        lock (_gate)
        {
            _world.MaxDegreeOfParallelism = threads;
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void EnsureThread()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Loop) { IsBackground = true, Name = "LifeSim.Engine" };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_stopping)
        {
            bool advanced = false;

            if (Interlocked.Exchange(ref _pendingSteps, 0) is var steps && steps > 0)
            {
                for (int i = 0; i < steps && TryAdvance(); i++)
                {
                    advanced = true;
                }
            }
            else if (_playing && TryAdvance())
            {
                advanced = true;
            }

            if (advanced)
            {
                _onFrame(CaptureSnapshot());
                if (_playing)
                {
                    Thread.Sleep(_delayMs);
                }
            }
            else
            {
                Thread.Sleep(15); // idle: paused, or extinct — don't spin
            }
        }
    }

    private bool TryAdvance()
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

    private WorldSnapshot CaptureSnapshot()
    {
        lock (_gate)
        {
            return _world.ToSnapshot();
        }
    }
}
