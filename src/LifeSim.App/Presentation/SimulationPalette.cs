using Avalonia.Media;
using LifeSim.Core.Organisms;
using LifeSim.Core.World;

namespace LifeSim.App.Presentation;

/// <summary>
/// The simulation colour tokens (lifesim.md §18), kept as their own set separate from the app's
/// Fluent chrome tokens so they can't drift from the sim spec. Colours are chosen to stay
/// distinguishable under common colour-vision deficiencies; the map never relies on colour alone —
/// action is also carried by outline + legend glyph, and every colour has a legend entry.
/// All members are pure functions of state, so a snapshot renders identically to a live frame.
/// </summary>
public static class SimulationPalette
{
    // --- Biome base colours (lifesim.md §18). ---
    public static readonly Color Grassland = Color.FromRgb(0x4C, 0x9A, 0x5A); // temperate green
    public static readonly Color Desert = Color.FromRgb(0xC9, 0xA2, 0x4B);    // hot amber/tan
    public static readonly Color Swamp = Color.FromRgb(0x1E, 0x5F, 0x63);     // murky dark teal
    public static readonly Color IceSheet = Color.FromRgb(0xD5, 0xE6, 0xEE);  // pale blue-white

    // --- Action palette (organism outline + Action fill mode) (lifesim.md §18). ---
    public static readonly Color Move = Color.FromRgb(0x3B, 0x7D, 0xD8);       // blue
    public static readonly Color Graze = Color.FromRgb(0x4C, 0xAF, 0x50);      // green
    public static readonly Color Predation = Color.FromRgb(0xE5, 0x48, 0x4D);  // red
    public static readonly Color Reproduce = Color.FromRgb(0xC4, 0x55, 0xC4);  // magenta
    public static readonly Color Share = Color.FromRgb(0x17, 0xB8, 0xC4);      // teal (cooperation)
    public static readonly Color Idle = Color.FromRgb(0x8A, 0x8F, 0x98);       // grey

    // --- Diet-tendency & neutral tokens. ---
    public static readonly Color DietGrazer = Graze;
    public static readonly Color DietPredator = Predation;
    public static readonly Color DietMixed = Idle;
    public static readonly Color Neutral = Color.FromRgb(0xB0, 0xB5, 0xBD);
    public static readonly Color Comfortable = Color.FromRgb(0xEA, 0xEC, 0xEE);

    // --- Stress-fit poles. ---
    public static readonly Color TooCold = Color.FromRgb(0x3B, 0x82, 0xF6); // blue
    public static readonly Color TooHot = Color.FromRgb(0xE5, 0x48, 0x4D);  // red

    // --- Energy gradient stops (0 → cap). ---
    private static readonly Color EnergyLow = Color.FromRgb(0xE5, 0x48, 0x4D);  // red
    private static readonly Color EnergyMid = Color.FromRgb(0xF2, 0xB1, 0x3A);  // amber
    private static readonly Color EnergyHigh = Color.FromRgb(0x3F, 0xB9, 0x50); // green

    public static Color Biome(Biome biome) => biome switch
    {
        Core.World.Biome.Grassland => Grassland,
        Core.World.Biome.Desert => Desert,
        Core.World.Biome.Swamp => Swamp,
        Core.World.Biome.IceSheet => IceSheet,
        _ => Neutral,
    };

    public static Color ActionColour(OrganismAction action) => action switch
    {
        OrganismAction.MoveNorth or OrganismAction.MoveSouth or OrganismAction.MoveEast or OrganismAction.MoveWest => Move,
        OrganismAction.HarvestSelf or OrganismAction.HarvestNorth or OrganismAction.HarvestSouth
            or OrganismAction.HarvestEast or OrganismAction.HarvestWest => Graze,
        OrganismAction.Reproduce => Reproduce,
        OrganismAction.ShareNorth or OrganismAction.ShareSouth
            or OrganismAction.ShareEast or OrganismAction.ShareWest => Share,
        OrganismAction.Idle => Idle,
        _ => Idle,
    };

    /// <summary>Energy fill (lifesim.md §18): red → amber → green across [0, <paramref name="ceiling"/>].</summary>
    public static Color EnergyColour(double energy, double ceiling)
    {
        double t = ceiling <= 0.0 ? 0.0 : Math.Clamp(energy / ceiling, 0.0, 1.0);
        return t < 0.5
            ? Lerp(EnergyLow, EnergyMid, t / 0.5)
            : Lerp(EnergyMid, EnergyHigh, (t - 0.5) / 0.5);
    }

    /// <summary>
    /// Stress-fit fill (lifesim.md §18): comfortable inside the thermal envelope, shading toward
    /// blue when the tile is below it and red when above, saturating over <paramref name="range"/> °C.
    /// </summary>
    public static Color StressFitColour(double tileTemperature, double thermalCenter, double thermalWidth, double range = 30.0)
    {
        double halfWidth = thermalWidth / 2.0;
        double below = (thermalCenter - halfWidth) - tileTemperature; // >0 → too cold
        double above = tileTemperature - (thermalCenter + halfWidth);  // >0 → too hot

        if (below <= 0.0 && above <= 0.0)
        {
            return Comfortable;
        }

        double magnitude = Math.Clamp(Math.Max(below, above) / range, 0.0, 1.0);
        return Lerp(Comfortable, below > above ? TooCold : TooHot, magnitude);
    }

    /// <summary>
    /// Ground-energy brightness modulation (lifesim.md §18): a tile at cap shows its full biome
    /// colour; a depleted tile darkens toward <paramref name="floor"/> of its brightness.
    /// </summary>
    public static Color ModulateByEnergy(Color biomeColour, double energy, double cap, double floor = 0.35)
    {
        double fill = cap <= 0.0 ? 1.0 : Math.Clamp(energy / cap, 0.0, 1.0);
        double scale = floor + ((1.0 - floor) * fill);
        return Color.FromRgb(
            (byte)Math.Round(biomeColour.R * scale),
            (byte)Math.Round(biomeColour.G * scale),
            (byte)Math.Round(biomeColour.B * scale));
    }

    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromArgb(
            (byte)Math.Round(a.A + ((b.A - a.A) * t)),
            (byte)Math.Round(a.R + ((b.R - a.R) * t)),
            (byte)Math.Round(a.G + ((b.G - a.G) * t)),
            (byte)Math.Round(a.B + ((b.B - a.B) * t)));
    }
}
