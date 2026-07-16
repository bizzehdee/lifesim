using System.Diagnostics;
using System.Threading.Tasks;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Engine;

/// <summary>
/// Drives a live <see cref="SimulationWorld"/> and pushes a lightweight frame to the <c>onFrame</c> sink
/// after every advance. Supports play / pause / frame-step / speed.
/// <para>
/// On desktop the loop runs on a dedicated background thread so the UI thread never blocks on ticks.
/// Under the browser (single-threaded WASM) real threads aren't available — starting one throws
/// <see cref="PlatformNotSupportedException"/> — so the identical loop instead runs cooperatively on the
/// main thread as an async task, <c>await</c>ing between ticks to yield to the browser event loop.
/// </para>
/// The sink marshals onto the UI thread (the desktop head wraps it in the Avalonia dispatcher); the Core
/// stays authoritative — this only calls <see cref="SimulationWorld.Advance"/> and
/// <see cref="SimulationWorld.ToFrame(long?)"/>. Full replayable checkpoints are captured only on demand.
/// </summary>
public sealed class EngineRunner : IDisposable
{
    private readonly Action<WorldFrame> _onFrame;
    private readonly Lock _gate = new();
    private readonly SimulationWorld _world;

    /// <summary>Cap on frame/render emission while playing (~30 fps), decoupled from the simulation rate.</summary>
    private const long FrameIntervalMs = 33;

    // Loop pacing/frame state — only ever touched by the single driving loop (thread or async task).
    private readonly Stopwatch _clock = new();
    private long _nextFrameAt;
    private bool _wasPlaying;

    private Thread? _thread;
    private Task? _asyncLoop;
    private bool _started;
    private volatile bool _playing;
    private volatile bool _stopping;
    private volatile int _pendingSteps;
    private volatile int _delayMs = 100; // target ms between ticks; 0.1 s/tick default
    private long _detailOrganismId = -1;

    public EngineRunner(SimulationWorld world, Action<WorldFrame> onFrame)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(onFrame);
        _world = world;
        _onFrame = onFrame;
        _onFrame(CaptureFrame()); // show the initial frame immediately
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

    /// <summary>Starts the driving loop (idempotent) and begins advancing.</summary>
    public void Play()
    {
        EnsureRunning();
        _playing = true;
    }

    public void Pause() => _playing = false;

    /// <summary>Requests exactly one tick while paused (queued, applied by the loop).</summary>
    public void Step()
    {
        EnsureRunning();
        Interlocked.Increment(ref _pendingSteps);
    }

    /// <summary>
    /// Sets the target wall-clock interval between ticks (seconds per tick). Each tick is paced off a
    /// stopwatch to hit this interval, so the rate is independent of CPU clock speed. The interval is a
    /// floor on the wait, never a busy-spin: if a tick costs more than the interval — e.g. the 1 ms
    /// target on a slow machine or a very large world — the sim just runs as fast as it can, so the
    /// fastest steps are best-effort targets rather than guarantees.
    /// </summary>
    public void SetTargetInterval(double secondsPerTick)
    {
        double ms = Math.Round(secondsPerTick * 1000.0);
        _delayMs = (int)Math.Clamp(ms, 1.0, int.MaxValue);
    }

    /// <summary>Live-adjust the per-tick worker thread count for the parallel phases (execution-only, results are unaffected).</summary>
    public void SetMaxDegreeOfParallelism(int threads)
    {
        lock (_gate)
        {
            _world.MaxDegreeOfParallelism = threads;
        }
    }

    /// <summary>Selects the one organism whose full live brain is carried in lightweight frames.</summary>
    public void SetDetailOrganismId(long? organismId)
    {
        long value = organismId ?? -1;
        if (Interlocked.Exchange(ref _detailOrganismId, value) != value)
        {
            _onFrame(CaptureFrame());
        }
    }

    /// <summary>Captures a full replayable checkpoint on demand for save/edit operations.</summary>
    public WorldSnapshot CaptureCheckpoint() => CaptureSnapshot();

    public void Dispose()
    {
        _stopping = true;
        _thread?.Join(TimeSpan.FromSeconds(2)); // the async browser loop just observes _stopping and exits
    }

    /// <summary>
    /// Starts the drive loop once. Under the browser there is no thread to spare, so the loop runs as a
    /// cooperative async task on the main thread; everywhere else it gets a dedicated background thread.
    /// </summary>
    private void EnsureRunning()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _clock.Start();

        if (OperatingSystem.IsBrowser())
        {
            _asyncLoop = RunAsyncLoopAsync();
        }
        else
        {
            _thread = new Thread(ThreadLoop) { IsBackground = true, Name = "LifeSim.Engine" };
            _thread.Start();
        }
    }

    // Desktop: a tight background-thread loop that sleeps the unspent per-tick remainder.
    private void ThreadLoop()
    {
        while (!_stopping)
        {
            int wait = PumpOnce();
            if (wait > 0)
            {
                Thread.Sleep(wait);
            }
        }
    }

    // Browser: the same loop on the main thread, awaiting between iterations so the browser stays
    // responsive (there is no background thread to fall back on). Always yields at least ~1 ms, which
    // caps the demo's flat-out rate but keeps the UI alive.
    private async Task RunAsyncLoopAsync()
    {
        while (!_stopping)
        {
            int wait = PumpOnce();
            await Task.Delay(Math.Max(1, wait)).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// One iteration of the drive loop, shared by both pumps. Returns the number of milliseconds to wait
    /// before the next iteration (0 = run again immediately).
    /// </summary>
    private int PumpOnce()
    {
        // Frame-step (while paused): advance the requested count and always emit, so a step is exactly
        // one visible tick.
        if (Interlocked.Exchange(ref _pendingSteps, 0) is var steps && steps > 0)
        {
            bool stepped = false;
            for (int i = 0; i < steps && TryAdvance(); i++)
            {
                stepped = true;
            }

            if (stepped)
            {
                _onFrame(CaptureFrame());
                _nextFrameAt = _clock.ElapsedMilliseconds + FrameIntervalMs;
            }

            return 0;
        }

        if (_playing)
        {
            long start = _clock.ElapsedMilliseconds;
            if (!TryAdvance())
            {
                _wasPlaying = false; // extinct — nothing more to run
                return 15;
            }

            _wasPlaying = true;

            // Emit at most ~30 fps regardless of how fast the sim ticks; rendering should not throttle it.
            long now = _clock.ElapsedMilliseconds;
            if (now >= _nextFrameAt)
            {
                _onFrame(CaptureFrame());
                _nextFrameAt = now + FrameIntervalMs;
            }

            // _delayMs is the *target* per-tick interval: wait only the unspent remainder, so the rate
            // is CPU-independent; if the tick already overran the interval, run the next one immediately.
            int remaining = _delayMs - (int)(now - start);
            return remaining > 0 ? remaining : 0;
        }

        // On the play→pause edge, emit the true latest state so the view (and frame-step) reflect exactly
        // where the sim stopped, not the last ~30 fps frame.
        if (_wasPlaying)
        {
            _onFrame(CaptureFrame());
            _wasPlaying = false;
        }

        return 15;
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

    private WorldFrame CaptureFrame()
    {
        lock (_gate)
        {
            long id = Interlocked.Read(ref _detailOrganismId);
            return _world.ToFrame(id >= 0 ? id : null);
        }
    }
}
