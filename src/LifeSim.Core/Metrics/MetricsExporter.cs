using System.Globalization;
using System.Text.Json;
using LifeSim.Core.Snapshot;

namespace LifeSim.Core.Metrics;

/// <summary>
/// Serializes per-tick <see cref="SimulationMetrics"/> for external plotting/batch analysis
///. Two formats:
/// <list type="bullet">
/// <item><b>NDJSON</b> — one full <see cref="MetricsSample"/> per line, including the nested
/// histograms and per-lineage reproduction that don't fit a flat table.</item>
/// <item><b>CSV</b> — the scalar time-series columns only (a stable, plottable header), with the
/// nested breakdowns omitted; every number is formatted invariant-culture so files are portable.</item>
/// </list>
/// </summary>
public static class MetricsExporter
{
    private static readonly string[] CsvColumns =
    [
        "tick", "population", "extinct",
        "births", "deaths",
        "successful_grazing", "failed_grazing", "successful_predation", "failed_predation",
        "successful_share", "failed_share", "kin_predation", "energy_shared",
        "energy_min", "energy_avg", "energy_max",
        "avg_size", "avg_speed_capacity", "avg_thermal_center", "avg_thermal_width",
        "avg_env_radius", "avg_org_radius", "avg_sensory_acuity", "avg_metabolic_efficiency", "avg_share_fraction", "avg_cell_count",
        "pop_grassland", "pop_desert", "pop_swamp", "pop_ice_sheet",
        "active_event_count",
    ];

    /// <summary>The CSV header row (no trailing newline), matching the column order of <see cref="CsvRow"/>.</summary>
    public static string CsvHeader() => string.Join(',', CsvColumns);

    /// <summary>One CSV row for a tick's metrics (no trailing newline), aligned to <see cref="CsvHeader"/>.</summary>
    public static string CsvRow(long tick, SimulationMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var fields = new[]
        {
            tick.ToString(CultureInfo.InvariantCulture),
            metrics.Population.ToString(CultureInfo.InvariantCulture),
            metrics.Extinct ? "true" : "false",
            metrics.Births.ToString(CultureInfo.InvariantCulture),
            metrics.Deaths.ToString(CultureInfo.InvariantCulture),
            metrics.SuccessfulGrazing.ToString(CultureInfo.InvariantCulture),
            metrics.FailedGrazing.ToString(CultureInfo.InvariantCulture),
            metrics.SuccessfulPredation.ToString(CultureInfo.InvariantCulture),
            metrics.FailedPredation.ToString(CultureInfo.InvariantCulture),
            metrics.SuccessfulShare.ToString(CultureInfo.InvariantCulture),
            metrics.FailedShare.ToString(CultureInfo.InvariantCulture),
            metrics.KinPredation.ToString(CultureInfo.InvariantCulture),
            Num(metrics.EnergyShared),
            Num(metrics.EnergyMin),
            Num(metrics.EnergyAverage),
            Num(metrics.EnergyMax),
            Num(metrics.TraitAverages.Size),
            Num(metrics.TraitAverages.SpeedCapacity),
            Num(metrics.TraitAverages.ThermalCenter),
            Num(metrics.TraitAverages.ThermalWidth),
            Num(metrics.TraitAverages.EnvRadius),
            Num(metrics.TraitAverages.OrgRadius),
            Num(metrics.TraitAverages.SensoryAcuity),
            Num(metrics.TraitAverages.MetabolicEfficiency),
            Num(metrics.TraitAverages.ShareFraction),
            Num(metrics.TraitAverages.CellCount),
            BiomeCount(metrics, World.Biome.Grassland).ToString(CultureInfo.InvariantCulture),
            BiomeCount(metrics, World.Biome.Desert).ToString(CultureInfo.InvariantCulture),
            BiomeCount(metrics, World.Biome.Swamp).ToString(CultureInfo.InvariantCulture),
            BiomeCount(metrics, World.Biome.IceSheet).ToString(CultureInfo.InvariantCulture),
            metrics.ActiveEvents.Count.ToString(CultureInfo.InvariantCulture),
        };

        return string.Join(',', fields);
    }

    /// <summary>One newline-delimited-JSON line for a tick's metrics (no trailing newline).</summary>
    public static string NdjsonLine(long tick, SimulationMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return JsonSerializer.Serialize(new MetricsSample(tick, metrics), MetricsJsonContext.Default.MetricsSample);
    }

    private static long BiomeCount(SimulationMetrics metrics, World.Biome biome)
    {
        foreach (BiomePopulation entry in metrics.PopulationByBiome)
        {
            if (entry.Biome == biome)
            {
                return entry.Count;
            }
        }

        return 0;
    }

    // Round-trippable invariant formatting; "R" avoids locale decimal commas and precision loss.
    private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
