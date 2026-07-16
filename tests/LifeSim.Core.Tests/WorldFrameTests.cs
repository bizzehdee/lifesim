using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class WorldFrameTests
{
    private static SimulationWorld NewWorld(int population = 40) =>
        SimulationWorld.CreateGenesis(
            new WorldState { Seed = 42, Width = 64, Height = 64 },
            SimulationConfig.Default with { InitialPopulation = population });

    [Fact]
    public void Frame_roundTrips_andOnlySelectedOrganismCarriesABrain()
    {
        SimulationWorld world = NewWorld();
        long selectedId = world.ToSnapshot().Organisms[3].OrganismId;

        WorldFrame frame = WorldFrameSerializer.Load(WorldFrameSerializer.Save(world.ToFrame(selectedId)));
        WorldSnapshot presentation = frame.ToPresentationSnapshot();

        Assert.Equal(selectedId, frame.DetailOrganism?.OrganismId);
        Assert.NotEmpty(presentation.Organisms.Single(o => o.OrganismId == selectedId).Brain.Connections);
        Assert.All(
            presentation.Organisms.Where(o => o.OrganismId != selectedId),
            organism => Assert.Empty(organism.Brain.Connections));
    }

    [Fact]
    public void Frame_isMateriallySmallerThanAReplayableCheckpoint()
    {
        SimulationWorld world = NewWorld();

        int checkpointBytes = System.Text.Encoding.UTF8.GetByteCount(SnapshotSerializer.Save(world.ToSnapshot()));
        int frameBytes = System.Text.Encoding.UTF8.GetByteCount(WorldFrameSerializer.Save(world.ToFrame()));

        Assert.True(frameBytes < checkpointBytes / 2,
            $"Expected frame below half the checkpoint size, but got {frameBytes} vs {checkpointBytes} bytes.");
    }
}
