using LifeSim.Core.Configuration;
using LifeSim.Core.Editing;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class SnapshotEditorTests
{
    private static WorldSnapshot GenesisSnapshot()
    {
        var config = SimulationConfig.Default with { InitialPopulation = 10 };
        return SimulationWorld.CreateGenesis(new WorldState { Seed = 42, Width = 48, Height = 48 }, config).ToSnapshot();
    }

    [Fact]
    public void SetOrganismEnergy_changesEnergyAndAppendsAnEditLogEntry()
    {
        WorldSnapshot snapshot = GenesisSnapshot();
        OrganismSnapshot target = snapshot.Organisms[0];
        double previous = target.Energy;

        WorldSnapshot edited = SnapshotEditor.SetOrganismEnergy(snapshot, target.OrganismId, 12.5, "test");

        Assert.Equal(12.5, edited.Organisms.Single(o => o.OrganismId == target.OrganismId).Energy);
        EditLogEntry entry = Assert.Single(edited.EditLog);
        Assert.Equal($"organism:{target.OrganismId}", entry.Target);
        Assert.Equal("energy", entry.Field);
        Assert.Equal("12.5", entry.NewValue);
        Assert.Equal("test", entry.Reason);

        // The original is untouched (edits are non-mutating).
        Assert.Equal(previous, snapshot.Organisms[0].Energy);
        Assert.Empty(snapshot.EditLog);
    }

    [Fact]
    public void EditedSnapshot_isAReplayableStartingPoint_andCarriesTheEditLogAcrossResume()
    {
        WorldSnapshot edited = SnapshotEditor.SetOrganismEnergy(GenesisSnapshot(), GenesisSnapshot().Organisms[0].OrganismId, 7.0);

        // Round-trips through the serializer...
        WorldSnapshot reloaded = SnapshotSerializer.Load(SnapshotSerializer.Save(edited));
        Assert.Equal(edited.EditLog, reloaded.EditLog);

        // ...and the edit log survives being loaded into the engine and re-snapshotted (provenance persists).
        WorldSnapshot resumed = SimulationWorld.FromSnapshot(edited).ToSnapshot();
        Assert.Single(resumed.EditLog);
        Assert.Equal(7.0, resumed.Organisms.Single(o => o.OrganismId == edited.Organisms[0].OrganismId).Energy);
    }

    [Fact]
    public void SetOrganismEnergy_throwsForAnUnknownOrganism()
    {
        WorldSnapshot snapshot = GenesisSnapshot();
        Assert.Throws<ArgumentException>(() => SnapshotEditor.SetOrganismEnergy(snapshot, organismId: -1, newEnergy: 5.0));
    }
}
