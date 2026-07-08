using System.Globalization;
using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.App.Presentation;

/// <summary>One labelled statistic in the global-statistics panel.</summary>
public sealed record StatRow(string Label, string Value);

/// <summary>A titled group of related statistics.</summary>
public sealed record StatSection(string Title, IReadOnlyList<StatRow> Rows);

/// <summary>
/// Builds the world-level statistics panel purely from a <see cref="WorldSnapshot"/>
/// — population and vital rates, the energy economy, foraging and cooperation flows, trait means, the
/// multicellular picture, biome spread, and active events. Like every other view it derives only from
/// snapshot/state fields, so it reads identically for a live frame or a loaded file.
/// </summary>
public static class GlobalStatistics
{
    /// <summary>
    /// A compact "at a glance" summary for the Info sidebar — the handful of headline numbers a user
    /// wants without opening the full statistics panel: where the run is, how it's doing, and the world
    /// it started from. A pure projection of the snapshot, like <see cref="Build"/>.
    /// </summary>
    public static IReadOnlyList<StatRow> Glance(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SimulationMetrics? m = snapshot.Metrics;

        return
        [
            new StatRow("Tick", Int(snapshot.Tick)),
            new StatRow("Population", Int(snapshot.Organisms.Count)),
            new StatRow("Status", m?.Extinct == true ? "Extinct" : "Alive"),
            new StatRow("Generation (deepest)", Int(MaxGeneration(snapshot))),
            new StatRow("Distinct lineages", Int(DistinctLineages(snapshot))),
            new StatRow("Births / deaths", m is null ? "—" : $"{m.Births} / {m.Deaths}"),
            new StatRow("Avg energy", m is null ? "—" : Num(m.EnergyAverage)),
            new StatRow("World", $"seed {snapshot.World.Seed} · {snapshot.World.Width}×{snapshot.World.Height}"),
        ];
    }

    public static IReadOnlyList<StatSection> Build(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SimulationMetrics? m = snapshot.Metrics;
        MulticellularConfig mc = snapshot.Configuration.Multicellular;

        var sections = new List<StatSection>
        {
            new("Overview",
            [
                new StatRow("Tick", Int(snapshot.Tick)),
                new StatRow("Population", Int(snapshot.Organisms.Count)),
                new StatRow("Status", snapshot.Metrics?.Extinct == true ? "Extinct" : "Alive"),
                new StatRow("Distinct lineages", Int(DistinctLineages(snapshot))),
                new StatRow("Deepest generation", Int(MaxGeneration(snapshot))),
            ]),
        };

        if (m is not null)
        {
            sections.Add(new StatSection("This tick",
            [
                new StatRow("Births", Int(m.Births)),
                new StatRow("Deaths", Int(m.Deaths)),
                new StatRow("Grazing (ok / fail)", $"{m.SuccessfulGrazing} / {m.FailedGrazing}"),
                new StatRow("Predation (ok / fail)", $"{m.SuccessfulPredation} / {m.FailedPredation}"),
                new StatRow("Shares (ok / fail)", $"{m.SuccessfulShare} / {m.FailedShare}"),
                new StatRow("Kin predation", Int(m.KinPredation)),
                new StatRow("Energy shared", Num(m.EnergyShared)),
            ]));

            // Inclusive fitness: is sharing kin-biased (kin selection at work) or indiscriminate?
            sections.Add(new StatSection("Kin selection",
            [
                new StatRow("Shares this tick (kin / non-kin)", $"{m.KinDirectedShares} / {m.NonKinShares}"),
                new StatRow("Kin-directed share", KinShareFraction(m)),
                new StatRow("Mean indirect fitness (lifetime)", Num(m.MeanHelpGiven)),
            ]));

            sections.Add(new StatSection("Energy",
            [
                new StatRow("Min", Num(m.EnergyMin)),
                new StatRow("Average", Num(m.EnergyAverage)),
                new StatRow("Max", Num(m.EnergyMax)),
            ]));

            sections.Add(new StatSection("Mean traits",
            [
                new StatRow("Size", Num(m.TraitAverages.Size)),
                new StatRow("Speed capacity", Num(m.TraitAverages.SpeedCapacity)),
                new StatRow("Thermal centre", Num(m.TraitAverages.ThermalCenter)),
                new StatRow("Thermal width", Num(m.TraitAverages.ThermalWidth)),
                new StatRow("Env radius", Num(m.TraitAverages.EnvRadius)),
                new StatRow("Org radius", Num(m.TraitAverages.OrgRadius)),
                new StatRow("Sensory acuity", Num(m.TraitAverages.SensoryAcuity)),
                new StatRow("Metabolic efficiency", Num(m.TraitAverages.MetabolicEfficiency)),
                new StatRow("Armour", Num(m.TraitAverages.Armour)),
                new StatRow("Evasion", Num(m.TraitAverages.Evasion)),
                new StatRow("Toxicity", Num(m.TraitAverages.Toxicity)),
                new StatRow("Plasticity", Num(m.TraitAverages.Plasticity)),
                new StatRow("Generosity", Num(m.TraitAverages.ShareFraction)),
            ]));
        }

        if (mc.Enabled)
        {
            sections.Add(new StatSection("Multicellularity",
            [
                new StatRow("Mean cell count", Num(m?.TraitAverages.CellCount ?? 1.0)),
                new StatRow("Largest body", $"{LargestBody(snapshot)} cells"),
                new StatRow("Multicellular", Percent(MulticellularFraction(snapshot))),
                new StatRow("Mean specialist types", Num(MeanSpecialists(snapshot))),
                new StatRow("Sterile-soma bodies", Int(SterileSomaCount(snapshot))),
            ]));
        }

        if (m is not null)
        {
            sections.Add(new StatSection("Population by biome",
            [
                new StatRow("Grassland", Int(BiomeCount(m, Biome.Grassland))),
                new StatRow("Desert", Int(BiomeCount(m, Biome.Desert))),
                new StatRow("Swamp", Int(BiomeCount(m, Biome.Swamp))),
                new StatRow("Ice Sheet", Int(BiomeCount(m, Biome.IceSheet))),
            ]));
        }

        sections.Add(new StatSection("Active events", ActiveEventRows(snapshot)));
        return sections;
    }

    private static IReadOnlyList<StatRow> ActiveEventRows(WorldSnapshot snapshot)
    {
        if (snapshot.EnvironmentModifiers.Count == 0)
        {
            return [new StatRow("None", "standard physics")];
        }

        return [.. snapshot.EnvironmentModifiers.Select(e => new StatRow(EventName(e.Type), $"{e.RemainingTicks} ticks left"))];
    }

    private static int DistinctLineages(WorldSnapshot snapshot)
    {
        var seen = new HashSet<long>();
        foreach (LineageSnapshot lineage in snapshot.Lineages)
        {
            seen.Add(lineage.LineageId);
        }

        return seen.Count;
    }

    private static int MaxGeneration(WorldSnapshot snapshot)
    {
        int max = 0;
        foreach (LineageSnapshot lineage in snapshot.Lineages)
        {
            max = Math.Max(max, lineage.GenerationDepth);
        }

        return max;
    }

    private static int LargestBody(WorldSnapshot snapshot)
    {
        MulticellularConfig config = snapshot.Configuration.Multicellular;
        double max = 1.0;
        foreach (OrganismSnapshot organism in snapshot.Organisms)
        {
            max = Math.Max(max, Morphology.CellCount(organism.Genome.ToGenome(), config));
        }

        return (int)Math.Floor(max);
    }

    private static double MulticellularFraction(WorldSnapshot snapshot)
    {
        if (snapshot.Organisms.Count == 0)
        {
            return 0.0;
        }

        MulticellularConfig config = snapshot.Configuration.Multicellular;
        int multicellular = 0;
        foreach (OrganismSnapshot organism in snapshot.Organisms)
        {
            if (Morphology.CellCount(organism.Genome.ToGenome(), config) >= 2.0)
            {
                multicellular++;
            }
        }

        return (double)multicellular / snapshot.Organisms.Count;
    }

    private static double MeanSpecialists(WorldSnapshot snapshot)
    {
        if (snapshot.Organisms.Count == 0)
        {
            return 0.0;
        }

        MulticellularConfig config = snapshot.Configuration.Multicellular;
        double sum = 0.0;
        foreach (OrganismSnapshot organism in snapshot.Organisms)
        {
            sum += Morphology.SpecialistCount(organism.Genome.ToGenome(), config);
        }

        return sum / snapshot.Organisms.Count;
    }

    private static int SterileSomaCount(WorldSnapshot snapshot)
    {
        MulticellularConfig config = snapshot.Configuration.Multicellular;
        int count = 0;
        foreach (OrganismSnapshot organism in snapshot.Organisms)
        {
            if (!Morphology.CanReproduce(organism.Genome.ToGenome(), config))
            {
                count++;
            }
        }

        return count;
    }

    private static long BiomeCount(SimulationMetrics metrics, Biome biome)
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

    private static string EventName(EventType type) => type switch
    {
        EventType.ResourceBlight => "Resource blight",
        EventType.DensityPlague => "Density plague",
        EventType.ClimaticAnomaly => "Climatic anomaly",
        _ => type.ToString(),
    };

    private static string KinShareFraction(SimulationMetrics m)
    {
        long total = m.KinDirectedShares + m.NonKinShares;
        return total > 0 ? Percent((double)m.KinDirectedShares / total) : "—";
    }

    private static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string Percent(double fraction) => (fraction * 100.0).ToString("F0", CultureInfo.InvariantCulture) + "%";
}
