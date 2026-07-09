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
    /// body-plan genome traits — cell count + six specialisation weights (§21). 2.0 widens the brain's
    /// sensory input vector (19 → 26) for the diurnal/seasonal light senses, so a pre-2.0 snapshot's
    /// brains are structurally incompatible and are hard-rejected on import (major bump).
    /// </summary>
    public const string SchemaVersion = "2.0";

    /// <summary>
    /// Configuration block version (semver). Validated alongside the schema version.
    /// 1.1 adds the cooperation toggle, evolvable generosity, and senescence knobs (§17, §20);
    /// 1.2 adds the multicellularity block (§21); 1.3 adds the environment-cycle block, per-biome
    /// light factor, and the photosynthesis toggle (additive — older configs load with defaults).
    /// </summary>
    public const string ConfigVersion = "1.3";

    /// <summary>Engine version that stamps snapshots it creates.</summary>
    public const string SimulationVersion = "0.1.0";
}
