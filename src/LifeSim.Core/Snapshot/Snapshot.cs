using System.Collections.Generic;
using System.Text.Json;
using LifeSim.Core.Configuration;

namespace LifeSim.Core.Snapshot;

/// <summary>
/// A self-describing, validated, replayable world state file (lifesim.md §12). Blocks that are
/// modelled in later phases (organisms, lineages, metrics, events, edit log) are carried as raw
/// JSON for now so files round-trip losslessly before those types exist.
/// </summary>
public sealed record WorldSnapshot
{
    public string SchemaVersion { get; init; } = BuildInfo.SchemaVersion;
    public string ConfigVersion { get; init; } = BuildInfo.ConfigVersion;
    public string SimulationVersion { get; init; } = BuildInfo.SimulationVersion;

    public long Tick { get; init; }

    public WorldState World { get; init; } = new();
    public SimulationConfig Configuration { get; init; } = SimulationConfig.Default;

    /// <summary>Full state of every deterministic stream, keyed by stream name (lifesim.md §9).</summary>
    public Dictionary<string, ulong[]> PrngStreams { get; init; } = [];

    public EvolutionBookkeeping EvolutionBookkeeping { get; init; } = new();

    // --- Blocks fleshed out in later phases; raw JSON keeps files round-trippable now. ---
    public List<JsonElement> EnvironmentModifiers { get; init; } = [];
    public List<JsonElement> Organisms { get; init; } = [];
    public List<JsonElement> Lineages { get; init; } = [];
    public JsonElement? Metrics { get; init; }
    public List<JsonElement> EditLog { get; init; } = [];
}
