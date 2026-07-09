using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LifeSim.App.Engine;
using LifeSim.Core.Brains;
using LifeSim.Core.Configuration;
using LifeSim.Core.Editing;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.ViewModels;

/// <summary>Where the rendered frames come from.</summary>
public enum SessionMode
{
    /// <summary>An in-process Core advancing on a background thread (desktop; small-world browser).</summary>
    Live,

    /// <summary>Frames streamed from a <c>sim serve</c> endpoint — canonical time is not advanced locally.</summary>
    Streaming,
}

/// <summary>One step on the discrete speed scale: a target seconds-per-tick interval and its display label.</summary>
public sealed record SpeedStep(double Seconds, string Label);

/// <summary>
/// The app-shell session hosting the shared <see cref="WorldViewModel"/>.
/// It exposes the control deck (play / pause / step / speed), save/load and stream exchange with the
/// console app, and the §16 edit flow. The <c>post</c> delegate marshals engine-thread frames onto
/// the UI thread (the heads pass the Avalonia dispatcher; tests pass a synchronous delegate). Desktop
/// runs <see cref="SessionMode.Live"/>; the browser demo defaults to loading/streaming and may opt
/// into a small live world.
/// </summary>
public partial class MainViewModel : ViewModelBase, IDisposable
{
    private const int MinDimension = 4;
    private const int MaxDimension = 1024;

    private readonly Action<Action> _post;
    private readonly bool _liveCapable;
    private readonly bool _autoStart;

    private EngineRunner? _runner;
    private SnapshotStreamClient? _stream;
    private CancellationTokenSource? _streamCts;
    private WorldSnapshot? _current;

    // Frame coalescing: the engine/stream produce frames on a background thread, but rendering one
    // (rebuild scene + inspector + charts + notifications) can cost more than the ~30 fps frame budget
    // on a large world. Without this, every produced frame is posted to the UI thread and they pile up
    // faster than they drain — the app falls progressively further behind ("huge lag"). Instead we keep
    // only the latest frame and post a single drain; intermediate frames are dropped, never queued.
    private WorldSnapshot? _pendingFrame;
    private int _frameQueued;

    [ObservableProperty]
    private SessionMode _mode = SessionMode.Live;

    [ObservableProperty]
    private bool _hasWorld;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>
    /// Discrete target intervals between ticks (seconds per tick), ordered slowest → fastest so sliding
    /// right runs faster. The engine paces each tick to hit its interval off a wall-clock, so the rate is
    /// independent of CPU clock speed; the fastest step (1 ms) is a best-effort target slower machines may
    /// not reach.
    /// </summary>
    public static readonly IReadOnlyList<SpeedStep> SpeedSteps =
    [
        new(10.0, "10 s / tick"),
        new(5.0, "5 s / tick"),
        new(1.0, "1 s / tick"),
        new(0.5, "0.5 s / tick"),
        new(0.1, "0.1 s / tick"),
        new(0.05, "50 ms / tick"),
        new(0.01, "10 ms / tick"),
        new(0.005, "5 ms / tick"),
        new(0.001, "1 ms / tick (target)"),
    ];

    /// <summary>Index into <see cref="SpeedSteps"/> selecting the target tick interval; default 0.1 s/tick.</summary>
    [ObservableProperty]
    private int _speedIndex = 4;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _streamUrl = "http://localhost:8080/";

    [ObservableProperty]
    private double _editEnergy = 50.0;

    // --- New-world setup fields (shown before the simulation starts); numeric spinners, whole numbers. ---
    [ObservableProperty]
    private decimal _seed = 42;

    [ObservableProperty]
    private decimal _width = 96;

    [ObservableProperty]
    private decimal _height = 96;

    [ObservableProperty]
    private decimal _population = 80;

    /// <summary>Cooperation feature set; on by default. Toggles Share actions + kin-predation deterrence for the new world.</summary>
    [ObservableProperty]
    private bool _cooperationEnabled = true;

    /// <summary>Aging model; on by default. When on, old organisms pay a growing metabolic tax.</summary>
    [ObservableProperty]
    private bool _senescenceEnabled = true;

    /// <summary>Multicellularity; on by default. Founders start unicellular and can evolve differentiated multi-cell bodies.</summary>
    [ObservableProperty]
    private bool _multicellularEnabled = true;

    /// <summary>
    /// When true, gameplay randomness (behaviour, mutation, combat, events) is seeded from entropy, so
    /// the same seed gives the same <em>map</em> but different <em>life</em> every run. Off = fully
    /// reproducible from the seed. Either way a created run still saves/reloads and replays identically.
    /// </summary>
    [ObservableProperty]
    private bool _entropyBehaviour;

    /// <summary>Brain-evaluation threads: 1..<see cref="MaxThreads"/>. Execution-only — results are identical for any value.</summary>
    [ObservableProperty]
    private decimal _threadCount = 1;

    /// <summary>Upper bound for <see cref="ThreadCount"/>: the machine's hardware threads.</summary>
    public int MaxThreads { get; } = Environment.ProcessorCount;

    /// <summary>Structured editor over every starting constant — the advanced setup panel binds to it, and it round-trips through save/load of options.</summary>
    [ObservableProperty]
    private AdvancedConfigEditor _advancedConfig = new(SnapshotSerializer.SaveConfig(SimulationConfig.Default));

    /// <summary>
    /// Founding-population composition by brain type: Generic (evolved) plus the shipped example scripts,
    /// each with a count. When any count is positive it seeds the world by type and the
    /// <see cref="Population"/> field is ignored; otherwise the flat <see cref="Population"/> of generics
    /// is used. Every type competes and evolves from tick 0.
    /// </summary>
    public ObservableCollection<BrainTypeRowViewModel> FoundingTypes { get; } = [];

    public WorldViewModel World { get; } = new();

    public bool LiveCapable => _liveCapable;

    /// <summary>Play is available only for a live world that isn't already running (so it can't be double-triggered).</summary>
    public bool CanPlay => HasWorld && LiveCapable && !IsPlaying;

    /// <summary>Pause is available only while a live world is actually running.</summary>
    public bool CanPause => HasWorld && LiveCapable && IsPlaying;

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));

        // While playing, cap the heavy Ranking/Organisms list rebuilds so a fast tick rate can't starve
        // the UI thread; paused/stepping refreshes them every frame for immediacy.
        World.ThrottleLiveLists = value;
    }

    partial void OnHasWorldChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
    }

    /// <summary>Highest valid <see cref="SpeedIndex"/> — the slider's upper bound.</summary>
    public int MaxSpeedIndex => SpeedSteps.Count - 1;

    /// <summary>Human label for the current speed step (e.g. "0.1 s / tick"), shown next to the slider.</summary>
    public string SpeedLabel => SpeedSteps[Math.Clamp(SpeedIndex, 0, MaxSpeedIndex)].Label;

    /// <summary>The − button can fine-tune down (slower) while not already at the slowest step.</summary>
    public bool CanSlowDown => SpeedIndex > 0;

    /// <summary>The + button can fine-tune up (faster) while not already at the fastest step.</summary>
    public bool CanSpeedUp => SpeedIndex < MaxSpeedIndex;

    partial void OnSpeedIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SpeedLabel));
        OnPropertyChanged(nameof(CanSlowDown));
        OnPropertyChanged(nameof(CanSpeedUp));
        _runner?.SetTargetInterval(SpeedSteps[Math.Clamp(value, 0, MaxSpeedIndex)].Seconds);
    }

    /// <summary>Fine-tune one step slower (a longer interval between ticks).</summary>
    [RelayCommand]
    public void SlowDown()
    {
        if (SpeedIndex > 0)
        {
            SpeedIndex--;
        }
    }

    /// <summary>Fine-tune one step faster (a shorter interval between ticks).</summary>
    [RelayCommand]
    public void SpeedUp()
    {
        if (SpeedIndex < MaxSpeedIndex)
        {
            SpeedIndex++;
        }
    }

    /// <summary>Design-time / default: live-capable, showing the setup screen.</summary>
    public MainViewModel()
        : this(liveEngine: true, autoStart: false, post: null)
    {
    }

    public MainViewModel(bool liveEngine, bool autoStart, Action<Action>? post, bool constrained = false)
    {
        _liveCapable = liveEngine;
        _autoStart = autoStart;
        _post = post ?? (action => action());

        Status = _liveCapable
            ? "Configure a new world, or load / connect to a stream."
            : "Load a snapshot or connect to a sim serve stream.";
        Mode = _liveCapable ? SessionMode.Live : SessionMode.Streaming;

        // The browser runs single-threaded WASM, so default to a small world there — a big grid is
        // painfully slow to tick. Desktop keeps the roomier default.
        if (constrained)
        {
            Width = 48;
            Height = 48;
            Population = 25;
        }

        // Fresh random seed each launch so a new world is different by default (still deterministic
        // once chosen — the user can pin any seed for reproducible runs).
        Seed = Random.Shared.Next(0, 1_000_000_000);

        // Default to half the machine's hardware threads (at least 1) — a balance between throughput
        // and leaving headroom for the UI and the rest of the system.
        ThreadCount = Math.Max(1, MaxThreads / 2);

        ResetFoundingTypes();
    }

    /// <summary>Populate the type list with Generic + the shipped example scripts, all at count 0.</summary>
    private void ResetFoundingTypes()
    {
        FoundingTypes.Clear();
        FoundingTypes.Add(new BrainTypeRowViewModel("Generic", script: null, count: 0, isGeneric: true, isRemovable: false));
        foreach (string script in BuiltInBrains.All)
        {
            string name = BrainScriptParser.ParseTemplate(script).Name;
            FoundingTypes.Add(new BrainTypeRowViewModel(name, script, count: 0, isGeneric: false, isRemovable: false));
        }
    }

    /// <summary>Adds a blank custom type the user can name and script.</summary>
    [RelayCommand]
    public void AddCustomType() =>
        FoundingTypes.Add(new BrainTypeRowViewModel(
            "Custom",
            "type Custom:\n  prefer HarvestToward(food)  always\n  prefer Reproduce            when ready\n",
            count: 0,
            isGeneric: false,
            isRemovable: true));

    /// <summary>Removes a custom type row (built-in rows are not removable).</summary>
    [RelayCommand]
    public void RemoveType(BrainTypeRowViewModel? row)
    {
        if (row is { IsRemovable: true })
        {
            FoundingTypes.Remove(row);
        }
    }

    [RelayCommand]
    public void Play()
    {
        if (_runner is null || IsPlaying)
        {
            return;
        }

        _runner.Play();
        IsPlaying = true;
    }

    [RelayCommand]
    public void Pause()
    {
        _runner?.Pause();
        IsPlaying = false;
    }

    [RelayCommand]
    public void Step()
    {
        _runner?.Step();
    }

    /// <summary>Returns to the new-world setup screen (stops any running/streamed world).</summary>
    [RelayCommand]
    public void ShowSetup()
    {
        DisposeSources();
        _current = null;
        IsPlaying = false;
        HasWorld = false;
        Status = "Configure a new world, or load / connect to a stream.";
    }

    /// <summary>Creates the world from the setup fields (seed, dimensions, population, and the full config JSON).</summary>
    [RelayCommand]
    public void CreateWorld()
    {
        if (FoundingTypes.Any(t => !t.IsValid))
        {
            Status = "Fix the invalid brain script(s) before creating the world.";
            return;
        }

        // Types with a positive count seed the world by composition (Population is then ignored);
        // an all-zero list leaves the composition empty so the flat Population of generics is used.
        var composition = FoundingTypes.Select(t => t.ToSpec()).OfType<BrainTypeSpec>().ToList();

        SimulationConfig config;
        try
        {
            SimulationConfig parsed = SnapshotSerializer.LoadConfig(AdvancedConfig.ToJson());
            config = parsed with
            {
                InitialPopulation = (int)Population,
                FoundingComposition = composition,
                Senescence = SenescenceEnabled,
                Cooperation = parsed.Cooperation with { Enabled = CooperationEnabled },
                Multicellular = parsed.Multicellular with { Enabled = MulticellularEnabled },
            };
        }
        catch (SnapshotValidationException ex)
        {
            Status = $"Invalid configuration: {ex.Message}";
            return;
        }

        if (composition.Count > 0)
        {
            long total = composition.Sum(s => (long)s.Count);
            if (total > (long)Width * (long)Height)
            {
                Status = "The founding types total more organisms than the world has tiles.";
                return;
            }
        }

        // Entropy mode seeds the gameplay streams from OS randomness (same map, different life each run).
        ulong? simulationSeed = EntropyBehaviour ? unchecked((ulong)Random.Shared.NextInt64()) : null;
        TryCreateWorld((ulong)Seed, (int)Width, (int)Height, config, out _, simulationSeed);
    }

    /// <summary>Pick a fresh random seed for the new world (the run stays deterministic once created).</summary>
    [RelayCommand]
    public void RandomiseSeed() => Seed = Random.Shared.Next(0, 1_000_000_000);

    /// <summary>Serialise the full setup — run parameters, headline toggles, and the whole config — for save-to-file.</summary>
    public string SaveOptionsJson()
    {
        var options = new JsonObject
        {
            ["seed"] = (long)Seed,
            ["width"] = (int)Width,
            ["height"] = (int)Height,
            ["population"] = (int)Population,
            ["threads"] = (int)ThreadCount,
            ["cooperation"] = CooperationEnabled,
            ["senescence"] = SenescenceEnabled,
            ["multicellular"] = MulticellularEnabled,
            ["entropy"] = EntropyBehaviour,
            ["founding_types"] = new JsonArray(FoundingTypes.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name,
                ["script"] = t.IsGeneric ? null : t.ScriptText,
                ["count"] = (int)t.Count,
                ["generic"] = t.IsGeneric,
                ["removable"] = t.IsRemovable,
                ["sexual"] = t.Sexual,
            }).ToArray()),
            ["config"] = JsonNode.Parse(AdvancedConfig.ToJson()),
        };

        return options.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Restore a setup previously saved with <see cref="SaveOptionsJson"/>; leaves the screen ready to create the world.</summary>
    public void LoadOptionsFromJson(string json)
    {
        JsonObject options;
        try
        {
            options = JsonNode.Parse(json)!.AsObject();
            AdvancedConfig = new AdvancedConfigEditor(options["config"]!.ToJsonString());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NullReferenceException or SnapshotValidationException)
        {
            Status = $"Invalid options file: {ex.Message}";
            return;
        }

        Seed = options["seed"]!.GetValue<long>();
        Width = options["width"]!.GetValue<int>();
        Height = options["height"]!.GetValue<int>();
        Population = options["population"]!.GetValue<int>();
        ThreadCount = ResolveThreads(options["threads"]!.GetValue<int>());
        CooperationEnabled = options["cooperation"]!.GetValue<bool>();
        SenescenceEnabled = options["senescence"]!.GetValue<bool>();
        MulticellularEnabled = options["multicellular"]!.GetValue<bool>();
        EntropyBehaviour = options["entropy"]?.GetValue<bool>() ?? false;

        if (options["founding_types"] is JsonArray savedTypes)
        {
            FoundingTypes.Clear();
            foreach (JsonNode? node in savedTypes)
            {
                if (node is not JsonObject t)
                {
                    continue;
                }

                bool generic = t["generic"]?.GetValue<bool>() ?? false;
                FoundingTypes.Add(new BrainTypeRowViewModel(
                    t["name"]?.GetValue<string>() ?? "Custom",
                    generic ? null : t["script"]?.GetValue<string>(),
                    t["count"]?.GetValue<int>() ?? 0,
                    generic,
                    t["removable"]?.GetValue<bool>() ?? false,
                    t["sexual"]?.GetValue<bool>() ?? false));
            }
        }
        else
        {
            ResetFoundingTypes(); // older options file without a composition
        }

        Status = "Loaded starting options — ready to create the world.";
    }

    /// <summary>Builds a genesis world from explicit parameters; returns false (with <paramref name="error"/>) on invalid input. A non-null <paramref name="simulationSeed"/> seeds gameplay randomness from it (entropy mode) instead of the world seed.</summary>
    public bool TryCreateWorld(ulong seed, int width, int height, SimulationConfig config, out string? error, ulong? simulationSeed = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (width is < MinDimension or > MaxDimension || height is < MinDimension or > MaxDimension)
        {
            error = $"Width and height must be between {MinDimension} and {MaxDimension}.";
            Status = error;
            return false;
        }

        if (config.InitialPopulation < 1 || config.InitialPopulation > (long)width * height)
        {
            error = "Population must be at least 1 and no more than the tile count.";
            Status = error;
            return false;
        }

        try
        {
            var world = SimulationWorld.CreateGenesis(new WorldState { Seed = seed, Width = width, Height = height }, config, simulationSeed);

            // Give the fresh timeline a root identity so later interventions are traceable to it.
            WorldSnapshot rooted = SnapshotProvenance.Root(world.ToSnapshot(), NewId("branch"), NewId("snap"));
            Adopt(rooted);
            Status = $"World ready: seed {seed}, {width}×{height}, population {world.Organisms.Count}.";
            if (_autoStart)
            {
                Play();
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or BrainScriptException)
        {
            // e.g. couldn't scatter the genesis population on enough Grassland tiles, or a founding
            // brain script failed to compile.
            error = ex.Message;
            Status = error;
            HasWorld = false;
            return false;
        }
    }

    [RelayCommand]
    public void Connect() => ConnectStream(StreamUrl);

    [RelayCommand]
    public void ApplyEdit() => EditSelectedEnergy(EditEnergy);

    /// <summary>The current frame as snapshot JSON, or null if none yet (world exchange with the console app).</summary>
    public string? CurrentJson() => _current is null ? null : SnapshotSerializer.Save(_current);

    /// <summary>Adopts a snapshot from JSON as a new (resumable, if live-capable) deterministic starting point.</summary>
    public void LoadFromJson(string json)
    {
        WorldSnapshot snapshot = SnapshotSerializer.Load(json);
        Adopt(snapshot);
        Status = $"Loaded snapshot at tick {snapshot.Tick}.";
    }

    /// <summary>Desktop convenience: save the current frame to a file path.</summary>
    public void SaveTo(string path)
    {
        if (CurrentJson() is { } json)
        {
            File.WriteAllText(path, json);
        }
    }

    /// <summary>Desktop convenience: load a snapshot from a file path.</summary>
    public void LoadFrom(string path) => LoadFromJson(File.ReadAllText(path));

    /// <summary>Connects to a <c>sim serve</c> endpoint and renders its stream (no local canonical time).</summary>
    public void ConnectStream(string baseUrl)
    {
        DisposeSources();
        Mode = SessionMode.Streaming;
        IsPlaying = false;
        _stream = new SnapshotStreamClient(baseUrl);
        _streamCts = new CancellationTokenSource();
        HasWorld = true;
        Status = $"Streaming from {baseUrl}.";
        _ = _stream.StreamAsync(PublishFrame, intervalMs: 200, _streamCts.Token);
    }

    /// <summary>Applies a §16 intervention to the selected organism and adopts the edited state.</summary>
    public void EditSelectedEnergy(double newEnergy, string? reason = null)
    {
        if (_current is null || World.SelectedOrganismId is not { } id)
        {
            return;
        }

        // An intervention forks a new comparable timeline rather than overwriting the original run
        //: edit the field, record it, then branch off the current snapshot.
        WorldSnapshot edited = SnapshotEditor.SetOrganismEnergy(_current, id, newEnergy, reason ?? "manual edit");
        edited = SnapshotProvenance.Branch(edited, NewId("branch"), NewId("snap"));

        if (Mode == SessionMode.Streaming && _stream is not null)
        {
            _ = _stream.PostAsync(edited);
            PublishFrame(edited);
        }
        else
        {
            Adopt(edited); // an edit is a new deterministic starting point
        }

        Status = $"Edited organism {id} energy → {newEnergy:F1}.";
    }

    public void Dispose() => DisposeSources();

    partial void OnThreadCountChanged(decimal value) => _runner?.SetMaxDegreeOfParallelism(ResolveThreads(value));

    /// <summary>Clamp a requested thread count to 1..hardware-threads.</summary>
    private int ResolveThreads(decimal requested) => Math.Clamp((int)requested, 1, MaxThreads);

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private void Adopt(WorldSnapshot snapshot) => Adopt(SimulationWorld.FromSnapshot(snapshot));

    private void Adopt(SimulationWorld world)
    {
        DisposeSources();
        Mode = SessionMode.Live;
        IsPlaying = false;
        World.ResetNotifications(); // fresh world → re-seed notification baselines silently
        world.MaxDegreeOfParallelism = ResolveThreads(ThreadCount);
        _runner = new EngineRunner(world, PublishFrame);
        _runner.SetTargetInterval(SpeedSteps[Math.Clamp(SpeedIndex, 0, MaxSpeedIndex)].Seconds);
        HasWorld = true;
    }

    private void PublishFrame(WorldSnapshot snapshot)
    {
        // Keep only the newest frame; post a drain only if one isn't already pending. See the field
        // comment on _pendingFrame — this bounds the UI-thread backlog to a single frame.
        Volatile.Write(ref _pendingFrame, snapshot);
        if (Interlocked.Exchange(ref _frameQueued, 1) == 0)
        {
            _post(DrainFrame);
        }
    }

    private void DrainFrame()
    {
        // Re-arm before reading so a frame produced during this render still schedules the next drain
        // (worst case an extra no-op drain, never a dropped final frame).
        Interlocked.Exchange(ref _frameQueued, 0);
        WorldSnapshot? snapshot = Interlocked.Exchange(ref _pendingFrame, null);
        if (snapshot is null)
        {
            return;
        }

        _current = snapshot;
        World.LoadSnapshot(snapshot);
    }

    private void DisposeSources()
    {
        _runner?.Dispose();
        _runner = null;
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = null;
        _stream?.Dispose();
        _stream = null;
    }
}
