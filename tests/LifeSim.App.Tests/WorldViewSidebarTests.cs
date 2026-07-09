using LifeSim.App.ViewModels;
using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Tests;

public class WorldViewSidebarTests
{
    private static WorldViewModel WorldWith20()
    {
        var world = SimulationWorld.CreateGenesis(
            new WorldState { Seed = 42, Width = 48, Height = 48 },
            SimulationConfig.Default with { InitialPopulation = 20 });
        var vm = new WorldViewModel();
        vm.LoadSnapshot(world.ToSnapshot());
        return vm;
    }

    [Fact]
    public void Glance_populatesHeadlineStats_afterASnapshot()
    {
        WorldViewModel vm = WorldWith20();

        Assert.NotEmpty(vm.Glance);
        Assert.Contains(vm.Glance, r => r.Label == "Generation (deepest)");
        Assert.Contains(vm.Glance, r => r.Label == "Population" && r.Value == "20");
        Assert.Contains(vm.Glance, r => r.Label == "World"); // start info (seed + dimensions)
        Assert.Contains(vm.Glance, r => r.Label == "Time" && r.Value.Contains("Day ")); // diurnal/seasonal clock
    }

    [Fact]
    public void ToggleLegend_opensAndClosesTheColoursPanel()
    {
        WorldViewModel vm = WorldWith20();
        Assert.False(vm.IsLegendVisible);

        vm.ToggleLegendCommand.Execute(null);
        Assert.True(vm.IsLegendVisible);

        vm.CloseLegendCommand.Execute(null);
        Assert.False(vm.IsLegendVisible);
    }
}
