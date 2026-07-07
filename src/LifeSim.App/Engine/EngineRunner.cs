using System.Diagnostics;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Engine;

/// <summary>
/// Drives a live <see cref="SimulationWorld"/> on a background thread: the UI thread
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

    /// <summary>Cap on snapshot/render emission while playing (~30 fps), decoupled from the sim rate so a fast sim isn't throttled by rebuilding the whole world every tick.</summary>
    private const long FrameIntervalMs = 33;

    private Thread? _thread;
    private volatile bool _playing;
    private volatile bool _stopping;
    private volatile int _pendingSteps;
    private volatile int _delayMs = 100; // minimum ms per tick; 10 ticks/s default

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
        // A minimum time per tick: at low speeds this paces the sim; at high speeds it's below the
        // compute time, so the sim runs flat-out (0 for the top of the range = fully unthrottled).
        _delayMs = clamped >= 1000.0 ? 0 : (int)Math.Round(1000.0 / clamped);
    }

    /// <summary>Live-adjust the brain-evaluation thread count (execution-only, results are unaffected).</summary>
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
        var clock = Stopwatch.StartNew();
        long nextFrameAt = 0;
        bool wasPlaying = false;

        while (!_stopping)
        {
            // Frame-step (while paused): advance the requested count and always emit, so a step is
            // exactly one visible tick.
            if (Interlocked.Exchange(ref _pendingSteps, 0) is var steps && steps > 0)
            {
                bool stepped = false;
                for (int i = 0; i < steps && TryAdvance(); i++)
                {
                    stepped = true;
                }

                if (stepped)
                {
                    _onFrame(CaptureSnapshot());
                    nextFrameAt = clock.ElapsedMilliseconds + FrameIntervalMs;
                }

                continue;
            }

            if (_playing)
            {
                long start = clock.ElapsedMilliseconds;
                if (!TryAdvance())
                {
                    wasPlaying = false; // extinct — nothing more to run
                    Thread.Sleep(15);
                    continue;
                }

                wasPlaying = true;

                // Emit at most ~30 fps regardless of how fast the sim ticks (a full snapshot + render of
                // the whole world every tick would itself throttle a large, fast sim).
                long now = clock.ElapsedMilliseconds;
                if (now >= nextFrameAt)
                {
                    _onFrame(CaptureSnapshot());
                    nextFrameAt = now + FrameIntervalMs;
                }

                // _delayMs is a *minimum* per-tick time: sleep only the unspent remainder, so a slow
                // speed paces the sim while a high speed runs flat-out and actually uses the CPU.
                int remaining = _delayMs - (int)(now - start);
                if (remaining > 0)
                {
                    Thread.Sleep(remaining);
                }
            }
            else
            {
                // On the play→pause edge, emit the true latest state so the view (and frame-step) reflect
                // exactly where the sim stopped, not the last ~30 fps frame.
                if (wasPlaying)
                {
                    _onFrame(CaptureSnapshot());
                    wasPlaying = false;
                }

                Thread.Sleep(15);
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
