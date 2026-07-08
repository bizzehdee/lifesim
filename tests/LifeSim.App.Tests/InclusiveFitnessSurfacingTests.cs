using LifeSim.App.Presentation;
using LifeSim.App.ViewModels;
using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Tests;

public class InclusiveFitnessSurfacingTests
{
    [Fact]
    public void RankingAndInspector_surfaceAnOrganismsHelpGiven()
    {
        // Run long enough for kin sharing to happen (same seed the Core share test uses), then confirm
        // the indirect-fitness tally reaches both the ranking and the inspector.
        var world = SimulationWorld.CreateGenesis(
            new WorldState { Seed = 909090, Width = 64, Height = 64 },
            SimulationConfig.Default with { InitialPopulation = 40 });
        for (int i = 0; i < 150 && !world.Extinct; i++)
        {
            world.Advance();
        }

        WorldSnapshot snapshot = world.ToSnapshot();
        OrganismSnapshot? helper = snapshot.Organisms.FirstOrDefault(o => o.HelpGiven > 0.0);
        Assert.NotNull(helper); // some living organism has donated to kin

        RankingEntry entry = RankingBuilder.Build(snapshot).First(r => r.OrganismId == helper!.OrganismId);
        Assert.Equal(helper!.HelpGiven, entry.HelpGiven);

        OrganismInspectorViewModel? inspector = OrganismInspectorViewModel.Create(snapshot, helper.OrganismId);
        Assert.NotNull(inspector);
        Assert.Equal(helper.HelpGiven, inspector!.HelpGiven);
    }

    [Fact]
    public void GlobalStatistics_hasAKinSelectionSection()
    {
        var world = SimulationWorld.CreateGenesis(
            new WorldState { Seed = 1, Width = 48, Height = 48 },
            SimulationConfig.Default with { InitialPopulation = 10 });

        StatSection kin = GlobalStatistics.Build(world.ToSnapshot()).First(s => s.Title == "Kin selection");

        Assert.Contains(kin.Rows, r => r.Label == "Shares this tick (kin / non-kin)");
        Assert.Contains(kin.Rows, r => r.Label == "Mean indirect fitness (lifetime)");
    }
}
