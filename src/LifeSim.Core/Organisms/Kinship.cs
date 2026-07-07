using LifeSim.Core.Configuration;

namespace LifeSim.Core.Organisms;

/// <summary>
/// Phenotypic relatedness between two genomes (lifesim.md §20). Computed as 1 minus the mean
/// per-trait difference normalized by each trait's bound span, clamped to [0, 1] — so identical
/// genomes (clones/close kin) read ~1 and divergent ones read lower. This is a self-contained
/// phenotype-matching proxy for kinship (no lineage bookkeeping needed), which suits an asexual
/// world where offspring are near-identical to their parent.
/// </summary>
public static class Kinship
{
    public static double Relatedness(Genome a, Genome b, TraitBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(bounds);

        double sum = 0.0;
        int count = 0;
        Accumulate(a.Size, b.Size, bounds.Size, ref sum, ref count);
        Accumulate(a.SpeedCapacity, b.SpeedCapacity, bounds.SpeedCapacity, ref sum, ref count);
        Accumulate(a.ThermalCenter, b.ThermalCenter, bounds.ThermalCenter, ref sum, ref count);
        Accumulate(a.ThermalWidth, b.ThermalWidth, bounds.ThermalWidth, ref sum, ref count);
        Accumulate(a.EnvRadius, b.EnvRadius, bounds.EnvRadius, ref sum, ref count);
        Accumulate(a.OrgRadius, b.OrgRadius, bounds.OrgRadius, ref sum, ref count);
        Accumulate(a.SensoryAcuity, b.SensoryAcuity, bounds.SensoryAcuity, ref sum, ref count);

        double meanDistance = count > 0 ? sum / count : 0.0;
        return Math.Clamp(1.0 - meanDistance, 0.0, 1.0);
    }

    private static void Accumulate(double a, double b, TraitBounds.Range range, ref double sum, ref int count)
    {
        double span = range.Max - range.Min;
        if (span > 0.0)
        {
            sum += Math.Min(1.0, Math.Abs(a - b) / span);
            count++;
        }
    }
}
