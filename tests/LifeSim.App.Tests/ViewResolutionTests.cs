using LifeSim.App;
using LifeSim.App.ViewModels;
using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.App.Tests;

/// <summary>
/// Guards the view-locator wiring: the inspector must resolve to <c>OrganismInspectorView</c>, not
/// fall back to showing the view-model's type name. Regression test for the inspector rendering only
/// "LifeSim.App.ViewModels.OrganismInspectorViewModel" — caused by the VM not deriving from
/// <see cref="ViewModelBase"/>, which the <see cref="ViewLocator"/> matches on.
/// </summary>
public class ViewResolutionTests
{
    private static OrganismInspectorViewModel Inspector()
    {
        WorldSnapshot snapshot = SimulationWorld.CreateGenesis(
            new WorldState { Seed = 42, Width = 48, Height = 48 },
            SimulationConfig.Default with { InitialPopulation = 10 }).ToSnapshot();
        return OrganismInspectorViewModel.Create(snapshot, snapshot.Organisms[0].OrganismId)!;
    }

    [Fact]
    public void ViewLocator_matchesTheOrganismInspectorViewModel()
    {
        var locator = new ViewLocator();
        Assert.True(locator.Match(Inspector()), "The inspector VM must be a ViewModelBase so the ViewLocator matches it.");
    }

    [Fact]
    public void OrganismInspectorView_typeExists_underTheNameTheLocatorComputes()
    {
        // The locator maps "…ViewModels.OrganismInspectorViewModel" → "…Views.OrganismInspectorView".
        Type? viewType = Type.GetType("LifeSim.App.Views.OrganismInspectorView, LifeSim.App");
        Assert.NotNull(viewType);
    }

    [Fact]
    public void LineageDetailView_resolvesForItsViewModel()
    {
        WorldSnapshot snapshot = SimulationWorld.CreateGenesis(
            new WorldState { Seed = 42, Width = 48, Height = 48 },
            SimulationConfig.Default with { InitialPopulation = 10 }).ToSnapshot();
        var detail = LineageDetailViewModel.Create(snapshot, snapshot.Organisms[0].OrganismId)!;

        Assert.True(new ViewLocator().Match(detail));
        Assert.NotNull(Type.GetType("LifeSim.App.Views.LineageDetailView, LifeSim.App"));
    }
}
