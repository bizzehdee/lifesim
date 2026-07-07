using LifeSim.Core.Configuration;
using LifeSim.Core.Editing;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Determinism.Tests;

/// <summary>
/// An edited world is a new deterministic starting point: resuming from an edited,
/// branched snapshot is itself byte-identical on replay, and the branch stays traceable to its parent.
/// </summary>
public class EditedReplayTests
{
    private const int Ticks = 60;

    private static WorldSnapshot EditedBranchedSnapshot()
    {
        var config = SimulationConfig.Default with { InitialPopulation = 30 };
        var world = SimulationWorld.CreateGenesis(new WorldState { Seed = 20240607, Width = 48, Height = 48 }, config);
        for (int i = 0; i < 20; i++)
        {
            world.Advance();
        }

        // Root the timeline, intervene on one organism, then fork a branch off that snapshot.
        WorldSnapshot root = SnapshotProvenance.Root(world.ToSnapshot(), "branch-root", "snap-root");
        WorldSnapshot edited = SnapshotEditor.SetOrganismEnergy(root, root.Organisms[0].OrganismId, 42.0, "replay test");
        return SnapshotProvenance.Branch(edited, "branch-1", "snap-1");
    }

    private static string RunFrom(WorldSnapshot start, int ticks)
    {
        var world = SimulationWorld.FromSnapshot(start);
        for (int i = 0; i < ticks && !world.Extinct; i++)
        {
            world.Advance();
        }

        return SnapshotSerializer.Save(world.ToSnapshot());
    }

    [Fact]
    public void EditedSnapshot_replaysByteIdenticallyFromThePointOfEdit()
    {
        WorldSnapshot edited = EditedBranchedSnapshot();

        // Same edited starting point, run twice → byte-identical (the edit is a deterministic seed).
        Assert.Equal(RunFrom(edited, Ticks), RunFrom(edited, Ticks));
    }

    [Fact]
    public void EditedSnapshot_carriesTheBranchProvenanceAndInterventionThroughTheResumedRun()
    {
        WorldSnapshot edited = EditedBranchedSnapshot();
        WorldSnapshot resumed = SnapshotSerializer.Load(RunFrom(edited, Ticks));

        Assert.Equal("branch-1", resumed.BranchId);
        Assert.Equal("snap-root", resumed.ParentSnapshotId); // traceable to the snapshot it forked from
        Assert.Single(resumed.EditLog);                      // the intervention is still recorded
    }

    [Fact]
    public void EditedSnapshot_divergesFromTheUneditedRun()
    {
        // The intervention actually changes the trajectory (otherwise the edit would be vacuous).
        var config = SimulationConfig.Default with { InitialPopulation = 30 };
        var world = SimulationWorld.CreateGenesis(new WorldState { Seed = 20240607, Width = 48, Height = 48 }, config);
        for (int i = 0; i < 20; i++)
        {
            world.Advance();
        }

        WorldSnapshot original = world.ToSnapshot();
        WorldSnapshot edited = SnapshotEditor.SetOrganismEnergy(original, original.Organisms[0].OrganismId, 0.0, "kill it");

        Assert.NotEqual(RunFrom(original, Ticks), RunFrom(edited, Ticks));
    }
}
