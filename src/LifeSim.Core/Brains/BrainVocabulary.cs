using LifeSim.Core.Organisms;
using LifeSim.Core.Sensing;

namespace LifeSim.Core.Brains;

/// <summary>
/// The token vocabulary a brain script draws on: the sensory <em>gates</em> a rule can key off and the
/// action <em>targets</em> it can push toward, each resolved to concrete sensory-input / action-output
/// indices for <see cref="BrainTemplateCompiler"/>. Kept data-driven and in one place so the authoring
/// language is easy to read, extend, and document — every entry here is a word an author may type.
/// </summary>
public static class BrainVocabulary
{
    /// <summary>A sensory gate: which input drives it, and whether the leaning grows as the input rises (+1) or falls (−1).</summary>
    public sealed record Gate(SensoryField Field, double Sign);

    /// <summary>
    /// A direction reference for a directional target macro: the two normalized direction inputs, plus an
    /// optional extra bias input (e.g. size-delta or relatedness) that skews the macro toward a kind of
    /// neighbour. Sign applies to the extra bias.
    /// </summary>
    public sealed record DirectionSource(SensoryField X, SensoryField Y, SensoryField? Extra, double ExtraSign);

    // Gate words: "when <gate>". Sign −1 means "fires more as the input falls" (e.g. hungry ← low energy).
    public static readonly IReadOnlyDictionary<string, Gate> Gates = new Dictionary<string, Gate>
    {
        ["always"] = new(SensoryField.Energy, 1.0),          // pseudo-bias: energy is ~always present (no bias node)
        ["ready"] = new(SensoryField.ReproductiveReadiness, 1.0),
        ["hungry"] = new(SensoryField.Energy, -1.0),
        ["full"] = new(SensoryField.Energy, 1.0),
        ["threatened"] = new(SensoryField.NearbyLargerCount, 1.0),
        ["crowded"] = new(SensoryField.LocalDensity, 1.0),
        ["kin_near"] = new(SensoryField.ClosestOrganismRelatedness, 1.0),
        ["stranger_near"] = new(SensoryField.ClosestOrganismRelatedness, -1.0),
        ["prey_near"] = new(SensoryField.NearbySmallerCount, 1.0),
        ["toxic_prey_near"] = new(SensoryField.ClosestOrganismToxicity, 1.0), // aposematic warning signal
        ["stressed"] = new(SensoryField.GlobalStressLevel, 1.0),
        ["bright"] = new(SensoryField.LightLevel, 1.0),   // fires in the light (daytime / summer / open ground)
        ["dark"] = new(SensoryField.LightLevel, -1.0),    // fires in the dark (night / winter / shade)
    };

    // Direction words usable inside a directional macro, e.g. HarvestToward(<dir>).
    public static readonly IReadOnlyDictionary<string, DirectionSource> Directions = new Dictionary<string, DirectionSource>
    {
        ["food"] = new(SensoryField.RichestTileDirectionX, SensoryField.RichestTileDirectionY, null, 0.0),
        ["nearest"] = new(SensoryField.ClosestOrganismDirectionX, SensoryField.ClosestOrganismDirectionY, null, 0.0),
        // Aim at the nearest organism, but skew toward smaller ones (size_delta < 0 ⇒ target smaller than me).
        ["smaller_neighbour"] = new(SensoryField.ClosestOrganismDirectionX, SensoryField.ClosestOrganismDirectionY, SensoryField.ClosestOrganismSizeDelta, -1.0),
        // Skew toward related / unrelated neighbours.
        ["kin"] = new(SensoryField.ClosestOrganismDirectionX, SensoryField.ClosestOrganismDirectionY, SensoryField.ClosestOrganismRelatedness, 1.0),
        ["stranger"] = new(SensoryField.ClosestOrganismDirectionX, SensoryField.ClosestOrganismDirectionY, SensoryField.ClosestOrganismRelatedness, -1.0),
        // Toward the brightest ground nearby — phototaxis / basking (or MoveAway for shade-seeking).
        ["light"] = new(SensoryField.LightDirectionX, SensoryField.LightDirectionY, null, 0.0),
    };

    /// <summary>The N/S/E/W action indices of a directional action family, for direction-vector wiring.</summary>
    public sealed record ActionFamily(OrganismAction North, OrganismAction South, OrganismAction East, OrganismAction West);

    public static readonly ActionFamily MoveFamily = new(OrganismAction.MoveNorth, OrganismAction.MoveSouth, OrganismAction.MoveEast, OrganismAction.MoveWest);
    public static readonly ActionFamily HarvestFamily = new(OrganismAction.HarvestNorth, OrganismAction.HarvestSouth, OrganismAction.HarvestEast, OrganismAction.HarvestWest);
    public static readonly ActionFamily ShareFamily = new(OrganismAction.ShareNorth, OrganismAction.ShareSouth, OrganismAction.ShareEast, OrganismAction.ShareWest);

    /// <summary>Plain (non-directional) target words.</summary>
    public static readonly IReadOnlyDictionary<string, OrganismAction> PlainActions = new Dictionary<string, OrganismAction>
    {
        ["reproduce"] = OrganismAction.Reproduce,
        ["idle"] = OrganismAction.Idle,
        ["harvestself"] = OrganismAction.HarvestSelf,
    };
}
