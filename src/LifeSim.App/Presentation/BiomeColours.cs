using Avalonia.Media;
using LifeSim.Core.Events;
using LifeSim.Core.World;

namespace LifeSim.App.Presentation;

/// <summary>
/// Resolves a tile's on-screen colour (lifesim.md §18): the biome base colour, darkened by how
/// depleted the tile's ground-energy pool is, then tinted by any active environmental event —
/// blight desaturates toward grey, a climatic anomaly applies a warm/cool overlay. (Plague zones
/// are drawn as a hatch overlay by the canvas, not a colour shift.) Biomes are reconstructed from
/// the world seed via the Core's Simplex, never read from the snapshot (lifesim.md §1, §2).
/// </summary>
public static class BiomeColours
{
    private static readonly Color BlightGrey = Color.FromRgb(0x7A, 0x7D, 0x82);
    private static readonly Color HeatOverlay = Color.FromRgb(0xE8, 0x6A, 0x2B);
    private static readonly Color ColdOverlay = Color.FromRgb(0x6A, 0xB8, 0xE8);

    /// <summary>
    /// The colour for tile (<paramref name="x"/>, <paramref name="y"/>). <paramref name="temperatureOffset"/>
    /// is the active climatic-anomaly shift (0 when none); positive warms, negative cools.
    /// </summary>
    public static Color Tile(
        Biome biome, double energy, double cap, bool blightActive, double temperatureOffset)
    {
        Color colour = SimulationPalette.ModulateByEnergy(SimulationPalette.Biome(biome), energy, cap);

        if (blightActive)
        {
            colour = SimulationPalette.Lerp(colour, BlightGrey, 0.55);
        }

        if (temperatureOffset > 0.0)
        {
            colour = SimulationPalette.Lerp(colour, HeatOverlay, 0.30);
        }
        else if (temperatureOffset < 0.0)
        {
            colour = SimulationPalette.Lerp(colour, ColdOverlay, 0.30);
        }

        return colour;
    }

    /// <summary>True when a plague is active — the canvas overlays a diagonal hatch on the whole map (lifesim.md §18).</summary>
    public static bool PlagueHatch(EnvironmentState environment) => environment.PlagueActive;
}
