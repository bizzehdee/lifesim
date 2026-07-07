using LifeSim.App.ViewModels;
using LifeSim.Core.Configuration;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Tests;

public class ConfigEditorTests
{
    private static AdvancedConfigEditor DefaultEditor() =>
        new(SnapshotSerializer.SaveConfig(SimulationConfig.Default));

    private static ConfigField Field(AdvancedConfigEditor editor, string group, string label) =>
        editor.Groups.First(g => g.Title == group).Fields.First(f => f.Label == label);

    [Fact]
    public void Editor_groupsEveryConfigBlock_withPrettyLabels()
    {
        AdvancedConfigEditor editor = DefaultEditor();

        Assert.Contains(editor.Groups, g => g.Title == "Metabolism");
        Assert.Contains(editor.Groups, g => g.Title == "Reproduction");
        Assert.Contains(editor.Groups, g => g.Title == "Multicellular");

        // Nested blocks become sub-groups (e.g. Trait Bounds → Size → Min/Max).
        ConfigGroup traitBounds = editor.Groups.First(g => g.Title == "Trait Bounds");
        Assert.Contains(traitBounds.Groups, g => g.Title == "Size");
    }

    [Fact]
    public void EditingANumberField_roundTripsThroughToJson()
    {
        AdvancedConfigEditor editor = DefaultEditor();

        ConfigField reproCost = Field(editor, "Reproduction", "Reproduction Base Cost");
        reproCost.Number = 5.5m;

        SimulationConfig parsed = SnapshotSerializer.LoadConfig(editor.ToJson());
        Assert.Equal(5.5, parsed.Reproduction.ReproductionBaseCost);
    }

    [Fact]
    public void EditingABoolField_roundTripsThroughToJson()
    {
        AdvancedConfigEditor editor = DefaultEditor();

        ConfigField requireDistinct = Field(editor, "Naming", "Require Distinct Adjectives");
        Assert.True(requireDistinct.IsBool);
        requireDistinct.Flag = true;

        SimulationConfig parsed = SnapshotSerializer.LoadConfig(editor.ToJson());
        Assert.True(parsed.Naming.RequireDistinctAdjectives);
    }

    [Fact]
    public void HeadlineToggles_areExcludedFromTheEditor_butPreservedInJson()
    {
        AdvancedConfigEditor editor = DefaultEditor();

        // Senescence / cooperation.enabled / multicellular.enabled are edited via the setup checkboxes.
        Assert.DoesNotContain(editor.Groups.SelectMany(AllFields), f => f.Label == "Senescence");
        Assert.DoesNotContain(editor.Groups.First(g => g.Title == "Cooperation").Fields, f => f.Label == "Enabled");

        // …but they still round-trip untouched.
        SimulationConfig parsed = SnapshotSerializer.LoadConfig(editor.ToJson());
        Assert.True(parsed.Cooperation.Enabled);
        Assert.True(parsed.Multicellular.Enabled);
    }

    [Fact]
    public void SaveThenLoadOptions_restoresEveryStartingField()
    {
        var a = new MainViewModel(liveEngine: true, autoStart: false, post: x => x());
        a.Seed = 12345;
        a.Width = 40;
        a.Height = 48;
        a.Population = 17;
        a.ThreadCount = 1;
        a.CooperationEnabled = false;
        a.SenescenceEnabled = false;
        a.MulticellularEnabled = false;
        Field(a.AdvancedConfig, "Reproduction", "Reproduction Base Cost").Number = 4.25m;

        string saved = a.SaveOptionsJson();

        var b = new MainViewModel(liveEngine: true, autoStart: false, post: x => x());
        b.LoadOptionsFromJson(saved);

        Assert.Equal(12345m, b.Seed);
        Assert.Equal(40m, b.Width);
        Assert.Equal(48m, b.Height);
        Assert.Equal(17m, b.Population);
        Assert.False(b.CooperationEnabled);
        Assert.False(b.SenescenceEnabled);
        Assert.False(b.MulticellularEnabled);
        Assert.Equal(4.25, SnapshotSerializer.LoadConfig(b.AdvancedConfig.ToJson()).Reproduction.ReproductionBaseCost);

        a.Dispose();
        b.Dispose();
    }

    private static IEnumerable<ConfigField> AllFields(ConfigGroup group) =>
        group.Fields.Concat(group.Groups.SelectMany(AllFields));
}
