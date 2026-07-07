using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LifeSim.App.Engine;
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

    [ObservableProperty]
    private SessionMode _mode = SessionMode.Live;

    [ObservableProperty]
    private bool _hasWorld;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _ticksPerSecond = 10.0;

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

    public WorldViewModel World { get; } = new();

    public bool LiveCapable => _liveCapable;

    /// <summary>Design-time / default: live-capable, showing the setup screen.</summary>
    public MainViewModel()
        : this(liveEngine: true, autoStart: false, post: null)
    {
    }

    public MainViewModel(bool liveEngine, bool autoStart, Action<Action>? post)
    {
        _liveCapable = liveEngine;
        _autoStart = autoStart;
        _post = post ?? (action => action());

        Status = _liveCapable
            ? "Configure a new world, or load / connect to a stream."
            : "Load a snapshot or connect to a sim serve stream.";
        Mode = _liveCapable ? SessionMode.Live : SessionMode.Streaming;

        // Fresh random seed each launch so a new world is different by default (still deterministic
        // once chosen — the user can pin any seed for reproducible runs).
        Seed = Random.Shared.Next(0, 1_000_000_000);

        // Default to half the machine's hardware threads (at least 1) — a balance between throughput
        // and leaving headroom for the UI and the rest of the system.
        ThreadCount = Math.Max(1, MaxThreads / 2);
    }

    [RelayCommand]
    public void Play()
    {
        if (_runner is null)
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
        SimulationConfig config;
        try
        {
            SimulationConfig parsed = SnapshotSerializer.LoadConfig(AdvancedConfig.ToJson());
            config = parsed with
            {
                InitialPopulation = (int)Population,
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
        catch (InvalidOperationException ex)
        {
            // e.g. couldn't scatter the genesis population on enough Grassland tiles.
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

    partial void OnTicksPerSecondChanged(double value) => _runner?.SetTicksPerSecond(value);

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
        _runner.SetTicksPerSecond(TicksPerSecond);
        HasWorld = true;
    }

    private void PublishFrame(WorldSnapshot snapshot) => _post(() =>
    {
        _current = snapshot;
        World.LoadSnapshot(snapshot);
    });

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
