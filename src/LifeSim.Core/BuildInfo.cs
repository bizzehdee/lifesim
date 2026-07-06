namespace LifeSim.Core;

/// <summary>
/// Version constants for the simulation engine. See lifesim.md §12 (Snapshot Schema &amp; Versioning).
/// These are the values stamped into every snapshot and validated on import.
/// </summary>
public static class BuildInfo
{
    /// <summary>Snapshot schema version (semver). Import hard-rejects on major mismatch (lifesim.md §12).</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Configuration block version (semver). Validated alongside the schema version (lifesim.md §12).</summary>
    public const string ConfigVersion = "1.0";

    /// <summary>Engine version that stamps snapshots it creates.</summary>
    public const string SimulationVersion = "0.1.0";
}
