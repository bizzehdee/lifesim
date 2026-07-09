using Avalonia.Media;
using LifeSim.Core.World;

namespace LifeSim.App.Presentation;

/// <summary>How a legend entry's colour is depicted, so the key never relies on colour alone.</summary>
public enum LegendGlyph
{
    Swatch,
    Ring,
    Pip,
    Hatch,
    Gradient,
}

/// <summary>One legend row: a label, its colour, and the glyph shape that carries it.</summary>
public sealed record LegendEntry(string Label, Color Colour, LegendGlyph Glyph = LegendGlyph.Swatch);

/// <summary>A titled group of legend rows.</summary>
public sealed record LegendSection(string Title, IReadOnlyList<LegendEntry> Entries);

/// <summary>
/// Builds the always-visible colour key. It reflects the active fill
/// <see cref="ColourMode"/> plus the always-on channels: last-action outlines, overlay glyphs, and
/// biome swatches (with the ground-energy brightness ramp). Nothing on the map is colour-coded
/// without a corresponding entry here.
/// </summary>
public static class LegendBuilder
{
    public static IReadOnlyList<LegendSection> Build(ColourMode mode)
    {
        return
        [
            FillSection(mode),
            new LegendSection("Last action (outline)",
            [
                new LegendEntry("Move", SimulationPalette.Move, LegendGlyph.Ring),
                new LegendEntry("Graze", SimulationPalette.Graze, LegendGlyph.Ring),
                new LegendEntry("Predation", SimulationPalette.Predation, LegendGlyph.Ring),
                new LegendEntry("Reproduce", SimulationPalette.Reproduce, LegendGlyph.Ring),
                new LegendEntry("Share", SimulationPalette.Share, LegendGlyph.Ring),
                new LegendEntry("Idle", SimulationPalette.Idle, LegendGlyph.Ring),
            ]),
            new LegendSection("Overlays",
            [
                new LegendEntry("Reproductive-ready", SimulationPalette.Reproduce, LegendGlyph.Pip),
                new LegendEntry("Thermal stress / event", SimulationPalette.TooHot, LegendGlyph.Ring),
                new LegendEntry("Predation flash", SimulationPalette.Predation, LegendGlyph.Swatch),
                new LegendEntry("Plague zone", SimulationPalette.Neutral, LegendGlyph.Hatch),
            ]),
            new LegendSection("Biomes (brightness ∝ ground energy)",
            [
                new LegendEntry("Grassland", SimulationPalette.Biome(Biome.Grassland)),
                new LegendEntry("Desert", SimulationPalette.Biome(Biome.Desert)),
                new LegendEntry("Swamp", SimulationPalette.Biome(Biome.Swamp)),
                new LegendEntry("Ice Sheet", SimulationPalette.Biome(Biome.IceSheet)),
            ]),
        ];
    }

    private static LegendSection FillSection(ColourMode mode) => mode switch
    {
        ColourMode.Action => new LegendSection("Fill: Action",
        [
            new LegendEntry("Move", SimulationPalette.Move),
            new LegendEntry("Graze", SimulationPalette.Graze),
            new LegendEntry("Predation", SimulationPalette.Predation),
            new LegendEntry("Reproduce", SimulationPalette.Reproduce),
            new LegendEntry("Share", SimulationPalette.Share),
            new LegendEntry("Idle", SimulationPalette.Idle),
        ]),
        ColourMode.Energy => new LegendSection("Fill: Energy",
        [
            new LegendEntry("Empty → full", SimulationPalette.EnergyColour(0, 100), LegendGlyph.Gradient),
        ]),
        ColourMode.DietTendency => new LegendSection("Fill: Diet tendency",
        [
            new LegendEntry("Grazer", SimulationPalette.DietGrazer),
            new LegendEntry("Predator", SimulationPalette.DietPredator),
            new LegendEntry("Mixed / neither", SimulationPalette.DietMixed),
        ]),
        ColourMode.StressFit => new LegendSection("Fill: Stress fit",
        [
            new LegendEntry("Too cold", SimulationPalette.TooCold),
            new LegendEntry("Comfortable", SimulationPalette.Comfortable),
            new LegendEntry("Too hot", SimulationPalette.TooHot),
        ]),
        ColourMode.Lineage => new LegendSection("Fill: Lineage",
        [
            new LegendEntry("Hashed colour per lineage", SimulationPalette.Neutral, LegendGlyph.Gradient),
        ]),
        ColourMode.Cooperation => new LegendSection("Fill: Cooperation",
        [
            new LegendEntry("Shared energy this tick", SimulationPalette.Share),
            new LegendEntry("Did not share", SimulationPalette.Neutral),
        ]),
        ColourMode.Intelligence => new LegendSection("Fill: Intelligence (brain capability)",
        [
            new LegendEntry("Simple → sophisticated", SimulationPalette.IntelligenceColour(80), LegendGlyph.Gradient),
        ]),
        _ => new LegendSection("Fill", []),
    };
}
