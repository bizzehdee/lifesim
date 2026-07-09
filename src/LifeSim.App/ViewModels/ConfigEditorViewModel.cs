using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LifeSim.App.ViewModels;

/// <summary>
/// One editable leaf of the simulation config: a labelled number or boolean
/// that writes straight back into the shared JSON tree, so <see cref="AdvancedConfigEditor.ToJson"/>
/// always reflects the latest edits. Whole values are written as integers (so integer config fields
/// round-trip); fractional values as doubles.
/// </summary>
public sealed partial class ConfigField : ObservableObject
{
    private readonly JsonObject _parent;
    private readonly string _key;

    public string Label { get; }
    public bool IsBool { get; }
    public decimal Increment { get; }
    public string Format { get; }

    [ObservableProperty]
    private decimal _number;

    [ObservableProperty]
    private bool _flag;

    public ConfigField(JsonObject parent, string key)
    {
        _parent = parent;
        _key = key;
        Label = Prettify(key);

        JsonValue value = (JsonValue)parent[key]!;
        if (value.TryGetValue(out bool boolean))
        {
            IsBool = true;
            _flag = boolean;
            Increment = 1;
            Format = "0";
            return;
        }

        double number = value.GetValue<double>();
        _number = (decimal)number;
        bool whole = Math.Abs(number - Math.Floor(number)) < 1e-9;
        Increment = whole ? 1m : 0.01m;
        Format = "0.######";
    }

    partial void OnNumberChanged(decimal value)
    {
        double d = (double)value;
        _parent[_key] = Math.Abs(d - Math.Floor(d)) < 1e-9 ? JsonValue.Create((long)d) : JsonValue.Create(d);
    }

    partial void OnFlagChanged(bool value) => _parent[_key] = JsonValue.Create(value);

    private static string Prettify(string snakeKey)
    {
        string[] words = snakeKey.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = char.ToUpper(words[i][0], CultureInfo.InvariantCulture) + words[i][1..];
        }

        return string.Join(' ', words);
    }
}

/// <summary>A titled group of config fields (a config sub-block, e.g. Metabolism), optionally with nested sub-groups.</summary>
public sealed record ConfigGroup(string Title, IReadOnlyList<ConfigField> Fields, IReadOnlyList<ConfigGroup> Groups);

/// <summary>
/// A structured editor over the entire simulation configuration. It parses the
/// config JSON into a mutable tree and exposes every numeric/boolean leaf as an editable
/// <see cref="ConfigField"/> grouped by config block, so the user can tweak any starting constant
/// without hand-editing JSON. Edits write straight back into the tree; <see cref="ToJson"/> returns
/// the current config. The headline toggles (cooperation/senescence/multicellularity/photosynthesis)
/// are excluded here because the setup screen surfaces them as dedicated checkboxes.
/// </summary>
public sealed class AdvancedConfigEditor
{
    private static readonly HashSet<string> Excluded = ["senescence", "photosynthesis", "cooperation.enabled", "multicellular.enabled"];

    private readonly JsonObject _root;

    public IReadOnlyList<ConfigGroup> Groups { get; }

    public AdvancedConfigEditor(string configJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configJson);
        _root = JsonNode.Parse(configJson)!.AsObject();

        (IReadOnlyList<ConfigField> fields, IReadOnlyList<ConfigGroup> groups) = Build(_root, prefix: "");
        var top = new List<ConfigGroup>();
        if (fields.Count > 0)
        {
            top.Add(new ConfigGroup("General", fields, []));
        }

        top.AddRange(groups);
        Groups = top;
    }

    /// <summary>The current configuration as JSON, reflecting every edit — the block <c>CreateWorld</c> consumes.</summary>
    public string ToJson() => _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static (IReadOnlyList<ConfigField> Fields, IReadOnlyList<ConfigGroup> Groups) Build(JsonObject obj, string prefix)
    {
        var fields = new List<ConfigField>();
        var groups = new List<ConfigGroup>();

        foreach ((string key, JsonNode? node) in obj)
        {
            string path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            if (Excluded.Contains(path))
            {
                continue;
            }

            switch (node)
            {
                case JsonObject child:
                    (IReadOnlyList<ConfigField> childFields, IReadOnlyList<ConfigGroup> childGroups) = Build(child, path);
                    groups.Add(new ConfigGroup(Prettify(key), childFields, childGroups));
                    break;
                case JsonValue value when value.TryGetValue(out bool _) || value.TryGetValue(out double _):
                    fields.Add(new ConfigField(obj, key));
                    break;
                default:
                    break; // strings / arrays / null — not tweakable numerically, left in the JSON untouched
            }
        }

        return (fields, groups);
    }

    private static string Prettify(string snakeKey)
    {
        string[] words = snakeKey.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = char.ToUpper(words[i][0], CultureInfo.InvariantCulture) + words[i][1..];
        }

        return string.Join(' ', words);
    }
}
