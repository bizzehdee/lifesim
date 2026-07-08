using LifeSim.App.ViewModels;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Tests;

public class BrainCompositionSetupTests
{
    private static MainViewModel NewSetup() => new(liveEngine: true, autoStart: false, post: a => a());

    [Fact]
    public void FoundingTypes_startWithGenericPlusExamples_allValidAtZero()
    {
        MainViewModel vm = NewSetup();

        Assert.Equal(8, vm.FoundingTypes.Count); // Generic + 7 built-in examples
        BrainTypeRowViewModel generic = vm.FoundingTypes[0];
        Assert.True(generic.IsGeneric);
        Assert.False(generic.HasScript);
        Assert.All(vm.FoundingTypes, t => Assert.True(t.IsValid));
        Assert.All(vm.FoundingTypes, t => Assert.Equal(0, (int)t.Count));
        Assert.Contains(vm.FoundingTypes, t => t.Name == "Selfish");
    }

    [Fact]
    public void AddCustomType_thenRemove_addsAndRemovesARemovableRow()
    {
        MainViewModel vm = NewSetup();

        vm.AddCustomType();
        BrainTypeRowViewModel custom = vm.FoundingTypes[^1];
        Assert.True(custom.IsRemovable);
        Assert.True(custom.HasScript);
        Assert.Equal(9, vm.FoundingTypes.Count);

        vm.RemoveType(custom);
        Assert.Equal(8, vm.FoundingTypes.Count);
    }

    [Fact]
    public void CustomRow_withBrokenScript_reportsAParseError()
    {
        var row = new BrainTypeRowViewModel("Broken", "type Broken:\n  prefer HarvestToward(unicorns)", 3, isGeneric: false, isRemovable: true);
        Assert.True(row.HasError);
        Assert.False(row.IsValid);

        row.ScriptText = "type Fixed:\n  prefer Reproduce when ready"; // live re-validation
        Assert.False(row.HasError);
        Assert.NotNull(row.ToSpec());
    }

    [Fact]
    public void CreateWorld_withComposition_seedsTheWorldByType()
    {
        MainViewModel vm = NewSetup();
        vm.Seed = 42;
        vm.Width = 48;
        vm.Height = 48;
        vm.FoundingTypes.First(t => t.Name == "Generic").Count = 5;
        vm.FoundingTypes.First(t => t.Name == "Selfish").Count = 5;

        vm.CreateWorld();

        WorldSnapshot? snapshot = vm.World.Snapshot;
        Assert.NotNull(snapshot);
        Dictionary<string, long> byType = snapshot!.Metrics!.PopulationByFoundingType.ToDictionary(p => p.Name, p => p.Count);
        Assert.Equal(5, byType["Generic"]);
        Assert.Equal(5, byType["Selfish"]);
        Assert.Equal(10, snapshot.Organisms.Count); // Population field ignored when composition is set
    }

    [Fact]
    public void SaveThenLoadOptions_roundTripsTheComposition()
    {
        MainViewModel saved = NewSetup();
        saved.FoundingTypes.First(t => t.Name == "Aggressor").Count = 12;
        saved.AddCustomType();
        saved.FoundingTypes[^1].Name = "Loner";
        saved.FoundingTypes[^1].Count = 3;
        string json = saved.SaveOptionsJson();

        MainViewModel loaded = NewSetup();
        loaded.LoadOptionsFromJson(json);

        Assert.Equal(12, (int)loaded.FoundingTypes.First(t => t.Name == "Aggressor").Count);
        BrainTypeRowViewModel loner = loaded.FoundingTypes.First(t => t.Name == "Loner");
        Assert.Equal(3, (int)loner.Count);
        Assert.True(loner.IsRemovable);
    }
}
