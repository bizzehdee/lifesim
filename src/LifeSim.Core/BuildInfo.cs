namespace LifeSim.Core;

/// <summary>
/// Version constants for the simulation engine — the snapshot schema &amp; versioning values.
/// These are the values stamped into every snapshot and validated on import.
/// </summary>
public static class BuildInfo
{
    /// <summary>
    /// Snapshot schema version (semver). Import hard-rejects on major mismatch.
    /// 1.1 adds the evolvable <c>share_fraction</c> genome trait (§20); 1.2 adds the multicellular
    /// body-plan genome traits — cell count + six specialisation weights (§21).
    /// </summary>
    public const string SchemaVersion = "1.2";

    /// <summary>
    /// Configuration block version (semver). Validated alongside the schema version.
    /// 1.1 adds the cooperation toggle, evolvable generosity, and senescence knobs (§17, §20);
    /// 1.2 adds the multicellularity block (§21).
    /// </summary>
    public const string ConfigVersion = "1.2";

    /// <summary>Engine version that stamps snapshots it creates.</summary>
    public const string SimulationVersion = "0.1.0";
}
