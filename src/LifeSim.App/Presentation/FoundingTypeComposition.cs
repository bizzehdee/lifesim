using Avalonia.Media;
using Avalonia.Media.Immutable;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Presentation;

/// <summary>One founding type's current standing: its live headcount and share of the population.</summary>
public sealed record FoundingTypeStanding(string Name, long Count, double SharePercent, IImmutableSolidColorBrush Swatch);

/// <summary>
/// A stacked-area composition-over-time chart: for each recorded sample, every type's normalized share
/// of the population (columns sum to ~1 while anyone is alive). <see cref="Types"/>/<see cref="Colours"/>
/// are aligned; <see cref="Shares"/> is indexed <c>[sample][type]</c>. The control turns this into
/// stacked bands, oldest sample on the left.
/// </summary>
public sealed record CompositionChart(
    IReadOnlyList<string> Types,
    IReadOnlyList<IImmutableSolidColorBrush> Colours,
    IReadOnlyList<double[]> Shares);

/// <summary>One recorded moment of the population's founding-type breakdown.</summary>
public sealed record FoundingTypeSample(long Tick, IReadOnlyDictionary<string, long> Counts);

/// <summary>
/// Turns snapshots and accumulated history into the founding-type scoreboard and the composition-over-
/// time chart. Colours are a deterministic function of the type name, so the chart bands and the
/// scoreboard swatches always match (and stay stable frame to frame) — the legend requirement.
/// </summary>
public static class FoundingTypeComposition
{
    /// <summary>A stable, well-spread colour for a type name (golden-angle hue, fixed saturation/lightness).</summary>
    public static Color Colour(string name)
    {
        uint hash = 2166136261u;
        foreach (char c in name)
        {
            hash = (hash ^ c) * 16777619u;
        }

        double hue = (hash % 360u) * 0.618_034 % 1.0 * 360.0; // golden-ratio spread avoids clustering
        return FromHsl(hue, 0.58, 0.60);
    }

    public static IImmutableSolidColorBrush Brush(string name) => new ImmutableSolidColorBrush(Colour(name));

    /// <summary>The current standings, highest headcount first (name as the stable tie-break).</summary>
    public static IReadOnlyList<FoundingTypeStanding> Standings(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IReadOnlyList<FoundingTypePopulation>? byType = snapshot.Metrics?.PopulationByFoundingType;
        if (byType is null || byType.Count == 0)
        {
            return [];
        }

        long total = byType.Sum(t => t.Count);
        return byType
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new FoundingTypeStanding(
                t.Name,
                t.Count,
                total > 0 ? 100.0 * t.Count / total : 0.0,
                Brush(t.Name)))
            .ToList();
    }

    /// <summary>Builds the stacked-area chart from history. Types are ordered by their latest headcount so the biggest band anchors the bottom.</summary>
    public static CompositionChart? Chart(IReadOnlyList<FoundingTypeSample> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count < 2)
        {
            return null; // nothing to trend yet
        }

        FoundingTypeSample latest = history[^1];
        List<string> types = history
            .SelectMany(s => s.Counts.Keys)
            .Distinct()
            .OrderByDescending(name => latest.Counts.GetValueOrDefault(name))
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToList();

        var shares = new List<double[]>(history.Count);
        foreach (FoundingTypeSample sample in history)
        {
            long total = sample.Counts.Values.Sum();
            var row = new double[types.Count];
            for (int i = 0; i < types.Count; i++)
            {
                row[i] = total > 0 ? (double)sample.Counts.GetValueOrDefault(types[i]) / total : 0.0;
            }

            shares.Add(row);
        }

        IReadOnlyList<IImmutableSolidColorBrush> colours = types.Select(Brush).ToList();
        return new CompositionChart(types, colours, shares);
    }

    private static Color FromHsl(double hueDegrees, double saturation, double lightness)
    {
        double c = (1.0 - Math.Abs((2.0 * lightness) - 1.0)) * saturation;
        double hp = hueDegrees / 60.0;
        double x = c * (1.0 - Math.Abs((hp % 2.0) - 1.0));
        (double r, double g, double b) = hp switch
        {
            < 1.0 => (c, x, 0.0),
            < 2.0 => (x, c, 0.0),
            < 3.0 => (0.0, c, x),
            < 4.0 => (0.0, x, c),
            < 5.0 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        double m = lightness - (c / 2.0);
        return Color.FromRgb(Channel(r + m), Channel(g + m), Channel(b + m));
    }

    private static byte Channel(double v) => (byte)Math.Clamp(Math.Round(v * 255.0), 0, 255);
}
