using System.Globalization;
using LifeSim.Core.Configuration;
using LifeSim.Core.World;

namespace LifeSim.App.Presentation;

/// <summary>
/// Human-readable descriptions of the <see cref="EnvironmentClock"/> for the UI — a compact
/// "Day 3 · midday · high summer" line and its component parts. Pure presentation over the deterministic
/// clock; the sim itself never sees these strings.
/// </summary>
public static class EnvironmentClockLabel
{
    private static readonly string[] TimeOfDay =
        ["midnight", "before dawn", "dawn", "morning", "midday", "afternoon", "dusk", "night"];

    // Season phase 0 = midwinter, 0.5 = midsummer; eight steps around the year.
    private static readonly string[] Season =
        ["deep winter", "early spring", "spring", "early summer", "high summer", "late summer", "autumn", "late autumn"];

    /// <summary>e.g. "☀ Day 3 · midday · high summer" — day count, time of day, season, with a sun/moon glyph.</summary>
    public static string Describe(long tick, EnvironmentCycleConfig cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        EnvironmentClock clock = EnvironmentClock.At(tick, cycle);
        string glyph = clock.GlobalLight >= 0.5 ? "☀" : "🌙";
        return $"{glyph} Day {DayNumber(tick, cycle)} · {TimeOfDayLabel(clock)} · {SeasonLabel(clock)}";
    }

    /// <summary>1-based day count since genesis.</summary>
    public static long DayNumber(long tick, EnvironmentCycleConfig cycle) =>
        (tick / Math.Max(1, cycle.DayLengthTicks)) + 1;

    public static string TimeOfDayLabel(EnvironmentClock clock) => TimeOfDay[Bucket(clock.DayPhase)];

    public static string SeasonLabel(EnvironmentClock clock) => Season[Bucket(clock.SeasonPhase)];

    /// <summary>Current light at a tile as a percentage string (global light × the tile's biome light factor).</summary>
    public static string LightPercent(double light) =>
        (Math.Clamp(light, 0.0, 1.0) * 100.0).ToString("F0", CultureInfo.InvariantCulture) + "%";

    // Map a 0..1 phase to one of eight labels (wraps cleanly at 1.0).
    private static int Bucket(double phase) => (int)(((phase % 1.0) + 1.0) % 1.0 * 8.0) & 7;
}
