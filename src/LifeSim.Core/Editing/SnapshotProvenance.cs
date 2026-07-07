using LifeSim.Core.Snapshot;

namespace LifeSim.Core.Editing;

/// <summary>
/// Branch provenance for interventions. An edit doesn't overwrite the original run;
/// it forks a new branch that records the snapshot it came from, so interventions form comparable
/// timelines. These ids are provenance metadata supplied by the UI — the deterministic engine only
/// carries them through, never mints them, so an untouched run stays byte-identical.
/// </summary>
public static class SnapshotProvenance
{
    /// <summary>Tags a snapshot as the root of a timeline (a fresh identity, no parent).</summary>
    public static WorldSnapshot Root(WorldSnapshot snapshot, string branchId, string snapshotId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot with { BranchId = branchId, SnapshotId = snapshotId, ParentSnapshotId = null };
    }

    /// <summary>
    /// Forks a new branch from <paramref name="snapshot"/>: the child records the parent's
    /// <see cref="WorldSnapshot.SnapshotId"/> as its <see cref="WorldSnapshot.ParentSnapshotId"/>,
    /// takes a fresh <paramref name="branchId"/> / <paramref name="snapshotId"/>, and leaves the
    /// original untouched.
    /// </summary>
    public static WorldSnapshot Branch(WorldSnapshot snapshot, string branchId, string snapshotId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot with
        {
            ParentSnapshotId = snapshot.SnapshotId,
            BranchId = branchId,
            SnapshotId = snapshotId,
        };
    }
}
