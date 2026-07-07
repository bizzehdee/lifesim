using System.Collections.Generic;
using LifeSim.Core.Events;
using LifeSim.Core.World;

namespace LifeSim.Core.Snapshot;

/// <summary>
/// Analytics as first-class output, recorded in the Metrics &amp; Snapshot phase
/// and carried in every snapshot's <c>metrics</c> block. Flow counters
/// (<see cref="Births"/>…<see cref="FailedPredation"/>) describe the single tick that produced this
/// snapshot; the remaining fields describe the population as it stands at snapshot time. All
/// cross-organism reductions are computed in ascending organism-id order for determinism.
/// </summary>
public sealed record SimulationMetrics
{
    public long Population { get; init; }

    /// <summary>Set once population reaches zero; the engine halts and never auto-reseeds.</summary>
    public bool Extinct { get; init; }

    // --- Per-tick flow counters (this tick's window). ---
    public long Births { get; init; }
    public long Deaths { get; init; }
    public long SuccessfulGrazing { get; init; }
    public long FailedGrazing { get; init; }
    public long SuccessfulPredation { get; init; }
    public long FailedPredation { get; init; }

    // --- Cooperation counters. ---
    public long SuccessfulShare { get; init; }
    public long FailedShare { get; init; }
    public long KinPredation { get; init; }
    public double EnergyShared { get; init; }

    // --- Energy distribution across the live population (zeros when empty). ---
    public double EnergyMin { get; init; }
    public double EnergyAverage { get; init; }
    public double EnergyMax { get; init; }

    /// <summary>Mean of every genome trait across the live population.</summary>
    public TraitAverages TraitAverages { get; init; } = new();

    /// <summary>One fixed-bin histogram per genome trait, binned across that trait's hard bounds.</summary>
    public List<TraitHistogram> TraitHistograms { get; init; } = [];

    /// <summary>Live population broken down by biome.</summary>
    public List<BiomePopulation> PopulationByBiome { get; init; } = [];

    /// <summary>Births attributed to each currently-living lineage.</summary>
    public List<LineageReproduction> ReproductionByLineage { get; init; } = [];

    /// <summary>The event types active this tick.</summary>
    public List<EventType> ActiveEvents { get; init; } = [];

    // Default record equality compares the List<> members by reference, so two structurally-equal
    // metrics deserialized into separate list instances would compare unequal. Use element/sequence
    // equality instead (same reasoning as NeatGenome).
    public bool Equals(SimulationMetrics? other) =>
        other is not null
        && Population == other.Population
        && Extinct == other.Extinct
        && Births == other.Births
        && Deaths == other.Deaths
        && SuccessfulGrazing == other.SuccessfulGrazing
        && FailedGrazing == other.FailedGrazing
        && SuccessfulPredation == other.SuccessfulPredation
        && FailedPredation == other.FailedPredation
        && SuccessfulShare == other.SuccessfulShare
        && FailedShare == other.FailedShare
        && KinPredation == other.KinPredation
        && EnergyShared.Equals(other.EnergyShared)
        && EnergyMin.Equals(other.EnergyMin)
        && EnergyAverage.Equals(other.EnergyAverage)
        && EnergyMax.Equals(other.EnergyMax)
        && TraitAverages == other.TraitAverages
        && TraitHistograms.SequenceEqual(other.TraitHistograms)
        && PopulationByBiome.SequenceEqual(other.PopulationByBiome)
        && ReproductionByLineage.SequenceEqual(other.ReproductionByLineage)
        && ActiveEvents.SequenceEqual(other.ActiveEvents);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Population);
        hash.Add(Extinct);
        hash.Add(Births);
        hash.Add(Deaths);
        hash.Add(EnergyAverage);
        hash.Add(TraitAverages);
        foreach (TraitHistogram histogram in TraitHistograms)
        {
            hash.Add(histogram);
        }

        foreach (BiomePopulation entry in PopulationByBiome)
        {
            hash.Add(entry);
        }

        foreach (LineageReproduction entry in ReproductionByLineage)
        {
            hash.Add(entry);
        }

        foreach (EventType active in ActiveEvents)
        {
            hash.Add(active);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Mean genome-trait values across the live population.</summary>
public sealed record TraitAverages
{
    public double Size { get; init; }
    public double SpeedCapacity { get; init; }
    public double ThermalCenter { get; init; }
    public double ThermalWidth { get; init; }
    public double EnvRadius { get; init; }
    public double OrgRadius { get; init; }
    public double SensoryAcuity { get; init; }

    /// <summary>Mean evolvable generosity — tracks whether the population drifts toward hoarding or over-sharing.</summary>
    public double ShareFraction { get; init; }

    /// <summary>Mean body size in cells — tracks the multicellular transition across the population.</summary>
    public double CellCount { get; init; } = 1.0;
}

/// <summary>
/// A fixed-bin histogram of one trait across the live population. Bins evenly
/// partition <c>[Min, Max]</c> (the trait's hard bounds); <see cref="Buckets"/> sums to the population.
/// </summary>
public sealed record TraitHistogram
{
    public string Trait { get; init; } = "";
    public double Min { get; init; }
    public double Max { get; init; }
    public List<int> Buckets { get; init; } = [];

    public bool Equals(TraitHistogram? other) =>
        other is not null
        && Trait == other.Trait
        && Min.Equals(other.Min)
        && Max.Equals(other.Max)
        && Buckets.SequenceEqual(other.Buckets);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Trait);
        hash.Add(Min);
        hash.Add(Max);
        foreach (int bucket in Buckets)
        {
            hash.Add(bucket);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Live population count in a single biome.</summary>
public sealed record BiomePopulation
{
    public Biome Biome { get; init; }
    public long Count { get; init; }
}

/// <summary>Cumulative births attributed to a lineage that still has living members.</summary>
public sealed record LineageReproduction
{
    public long LineageId { get; init; }
    public long Births { get; init; }
}
