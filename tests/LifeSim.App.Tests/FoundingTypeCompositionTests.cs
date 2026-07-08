using Avalonia.Media;
using LifeSim.App.Presentation;
using LifeSim.App.ViewModels;
using LifeSim.Core.Brains;
using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.App.Tests;

public class FoundingTypeCompositionTests
{
    private static SimulationWorld TypedWorld() => SimulationWorld.CreateGenesis(
        new WorldState { Seed = 42, Width = 48, Height = 48 },
        SimulationConfig.Default with
        {
            FoundingComposition =
            [
                new BrainTypeSpec { Name = "Generic", Count = 6 },
                new BrainTypeSpec { Name = "Selfish", Script = BuiltInBrains.Selfish, Count = 4 },
            ],
        });

    [Fact]
    public void Standings_rankByHeadcount_withMatchingShares()
    {
        WorldSnapshot snapshot = TypedWorld().ToSnapshot();

        IReadOnlyList<FoundingTypeStanding> standings = FoundingTypeComposition.Standings(snapshot);

        Assert.Equal("Generic", standings[0].Name); // 6 > 4, biggest first
        Assert.Equal(6, standings[0].Count);
        Assert.Equal(10, standings.Sum(s => s.Count));
        Assert.Equal(60.0, standings[0].SharePercent, 3);
    }

    [Fact]
    public void Colour_isDeterministicPerName_andDiffersBetweenNames()
    {
        Assert.Equal(FoundingTypeComposition.Colour("Selfish"), FoundingTypeComposition.Colour("Selfish"));
        Assert.NotEqual(FoundingTypeComposition.Colour("Selfish"), FoundingTypeComposition.Colour("Selfless"));
    }

    [Fact]
    public void Chart_normalisesSharesPerSample_orderedByLatestHeadcount()
    {
        var history = new List<FoundingTypeSample>
        {
            new(0, new Dictionary<string, long> { ["A"] = 1, ["B"] = 1 }),
            new(1, new Dictionary<string, long> { ["A"] = 3, ["B"] = 1 }),
        };

        CompositionChart? chart = FoundingTypeComposition.Chart(history);

        Assert.NotNull(chart);
        Assert.Equal("A", chart!.Types[0]); // A leads at the latest sample
        Assert.Equal(0.75, chart.Shares[1][0], 3); // A is 3 of 4
        Assert.Equal(0.25, chart.Shares[1][1], 3);
    }

    [Fact]
    public void Chart_needsAtLeastTwoSamples()
    {
        var one = new List<FoundingTypeSample> { new(0, new Dictionary<string, long> { ["A"] = 1 }) };
        Assert.Null(FoundingTypeComposition.Chart(one));
    }

    [Fact]
    public void WorldViewModel_accumulatesHistory_andFlagsTheBreakdown()
    {
        SimulationWorld world = TypedWorld();
        var vm = new WorldViewModel();

        vm.LoadSnapshot(world.ToSnapshot()); // tick 0 — one sample, no trend yet
        Assert.True(vm.HasFoundingTypeBreakdown);
        Assert.NotEmpty(vm.FoundingTypeStandings);
        Assert.Null(vm.FoundingTypeChart);

        world.Advance();
        vm.LoadSnapshot(world.ToSnapshot()); // tick 1 — now there's a trend
        Assert.NotNull(vm.FoundingTypeChart);
        Assert.Equal(2, vm.FoundingTypeChart!.Shares.Count);
    }

    [Fact]
    public void Inspector_surfacesTheOrganismFoundingType()
    {
        WorldSnapshot snapshot = TypedWorld().ToSnapshot();
        OrganismSnapshot org = snapshot.Organisms[0];

        OrganismInspectorViewModel? inspector = OrganismInspectorViewModel.Create(snapshot, org.OrganismId);

        string expected = snapshot.Lineages.First(l => l.OrganismId == org.OrganismId).FoundingType;
        Assert.NotNull(inspector);
        Assert.Equal(expected, inspector!.FoundingType);
        Assert.Contains(expected, new[] { "Generic", "Selfish" });
    }
}
