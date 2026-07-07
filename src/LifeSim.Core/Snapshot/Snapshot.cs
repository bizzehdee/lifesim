using System.Collections.Generic;
using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.World;

namespace LifeSim.Core.Snapshot;

/// <summary>
/// A self-describing, validated, replayable world state file. The edit-log block
/// is carried as raw JSON until Phase 15 gives it a type, so files still round-trip losslessly.
/// </summary>
public sealed record WorldSnapshot
{
    public string SchemaVersion { get; init; } = BuildInfo.SchemaVersion;
    public string ConfigVersion { get; init; } = BuildInfo.ConfigVersion;
    public string SimulationVersion { get; init; } = BuildInfo.SimulationVersion;

    // --- Branch provenance; null for an untouched deterministic run. Set only by
    // explicit UI branch actions, never by the engine, so genesis/replay stays byte-identical. ---

    /// <summary>Identifier of this snapshot, so a branch can point back at the snapshot it forked from.</summary>
    public string? SnapshotId { get; init; }

    /// <summary>The snapshot this branch was forked from, forming a traceable timeline.</summary>
    public string? ParentSnapshotId { get; init; }

    /// <summary>The timeline this world belongs to; interventions fork a new branch rather than overwriting the original.</summary>
    public string? BranchId { get; init; }

    public long Tick { get; init; }

    public WorldState World { get; init; } = new();
    public SimulationConfig Configuration { get; init; } = SimulationConfig.Default;

    /// <summary>Full state of every deterministic stream, keyed by stream name.</summary>
    public Dictionary<string, ulong[]> PrngStreams { get; init; } = [];

    public EvolutionBookkeeping EvolutionBookkeeping { get; init; } = new();

    /// <summary>Sparse ground-energy overrides — tiles omitted here are implicitly at their biome cap.</summary>
    public List<GroundEnergyEntry> GroundEnergy { get; init; } = [];

    /// <summary>Optional cached terrain window for inspection/debugging; never required for replay.</summary>
    public List<DebugTileEntry>? DebugTerrain { get; init; }

    public List<OrganismSnapshot> Organisms { get; init; } = [];

    /// <summary>The tick's analytics — population, flow counters, distributions, active events.</summary>
    public SimulationMetrics? Metrics { get; init; }

    public List<LineageSnapshot> Lineages { get; init; } = [];

    /// <summary>Active stochastic event modifiers; empty when the world is under standard physics.</summary>
    public List<EnvironmentModifier> EnvironmentModifiers { get; init; } = [];

    /// <summary>Explicit UI interventions applied to this world; empty for an untouched run.</summary>
    public List<EditLogEntry> EditLog { get; init; } = [];
}
