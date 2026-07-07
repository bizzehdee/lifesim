using LifeSim.Core.Configuration;
using LifeSim.Core.Editing;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class SnapshotProvenanceTests
{
    private static WorldSnapshot Genesis() =>
        SimulationWorld.CreateGenesis(
            new WorldState { Seed = 42, Width = 48, Height = 48 },
            SimulationConfig.Default with { InitialPopulation = 10 }).ToSnapshot();

    [Fact]
    public void Genesis_hasNoProvenanceIds_soUntouchedRunsStayByteIdentical()
    {
        WorldSnapshot snapshot = Genesis();
        Assert.Null(snapshot.SnapshotId);
        Assert.Null(snapshot.ParentSnapshotId);
        Assert.Null(snapshot.BranchId);
    }

    [Fact]
    public void Root_assignsAFreshIdentityWithNoParent()
    {
        WorldSnapshot root = SnapshotProvenance.Root(Genesis(), "branch-r", "snap-r");
        Assert.Equal("branch-r", root.BranchId);
        Assert.Equal("snap-r", root.SnapshotId);
        Assert.Null(root.ParentSnapshotId);
    }

    [Fact]
    public void Branch_pointsAtTheParentSnapshotAndTakesAFreshBranchId()
    {
        WorldSnapshot root = SnapshotProvenance.Root(Genesis(), "branch-r", "snap-r");
        WorldSnapshot child = SnapshotProvenance.Branch(root, "branch-1", "snap-1");

        Assert.Equal("branch-1", child.BranchId);
        Assert.Equal("snap-1", child.SnapshotId);
        Assert.Equal("snap-r", child.ParentSnapshotId); // traceable to its parent

        // Original untouched.
        Assert.Equal("branch-r", root.BranchId);
        Assert.Null(root.ParentSnapshotId);
    }

    [Fact]
    public void ProvenanceIds_roundTripAndSurviveResume()
    {
        WorldSnapshot branched = SnapshotProvenance.Branch(
            SnapshotProvenance.Root(Genesis(), "branch-r", "snap-r"), "branch-1", "snap-1");

        WorldSnapshot reloaded = SnapshotSerializer.Load(SnapshotSerializer.Save(branched));
        Assert.Equal("branch-1", reloaded.BranchId);
        Assert.Equal("snap-r", reloaded.ParentSnapshotId);

        // The engine carries them across a load → re-snapshot, never dropping provenance.
        WorldSnapshot resumed = SimulationWorld.FromSnapshot(branched).ToSnapshot();
        Assert.Equal("branch-1", resumed.BranchId);
        Assert.Equal("snap-1", resumed.SnapshotId);
        Assert.Equal("snap-r", resumed.ParentSnapshotId);
    }
}
