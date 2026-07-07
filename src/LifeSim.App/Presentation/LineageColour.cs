using Avalonia.Media;

namespace LifeSim.App.Presentation;

/// <summary>
/// A stable, deterministic colour per <c>lineage_id</c> (lifesim.md §18) so clonal clusters and
/// speciation read at a glance. The id is hashed to a hue at fixed saturation/lightness, so the
/// same lineage always maps to the same colour across frames, runs, and targets.
/// </summary>
public static class LineageColour
{
    public static Color ForLineage(long lineageId)
    {
        // SplitMix64-style avalanche so adjacent ids get well-separated hues.
        ulong x = (ulong)lineageId + 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        x ^= x >> 31;

        double hue = x % 360_000UL / 1000.0; // 0..360
        return new HslColor(1.0, hue, 0.62, 0.55).ToRgb();
    }
}
