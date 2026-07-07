using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;

namespace LifeSim.Core.Organisms;

/// <summary>
/// Applies bounded, inheritable trait drift to an offspring genome (lifesim.md §8). Each trait
/// mutates independently with probability <see cref="MutationConfig.TraitMutationRate"/>; the
/// perturbation is a uniform delta scaled to a fraction (<see cref="MutationConfig.TraitMutationDelta"/>)
/// of that trait's own bound span, so a single config value drifts every trait proportionally
/// regardless of its absolute range. All draws come from the mutation PRNG stream (lifesim.md §9),
/// in a fixed trait order, and the result is hard-clamped to <see cref="TraitBounds"/> (lifesim.md §3, §8).
/// </summary>
public static class GenomeMutator
{
    public static Genome Mutate(Genome genome, MutationConfig config, TraitBounds bounds, Prng mutationStream)
    {
        ArgumentNullException.ThrowIfNull(genome);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(mutationStream);

        // Object-initializer members are evaluated in source order, so the mutation-stream draws
        // happen in a fixed, reproducible trait sequence (lifesim.md §9).
        return new Genome
        {
            Size = Drift(genome.Size, bounds.Size, config, mutationStream),
            SpeedCapacity = Drift(genome.SpeedCapacity, bounds.SpeedCapacity, config, mutationStream),
            ThermalCenter = Drift(genome.ThermalCenter, bounds.ThermalCenter, config, mutationStream),
            ThermalWidth = Drift(genome.ThermalWidth, bounds.ThermalWidth, config, mutationStream),
            EnvRadius = Drift(genome.EnvRadius, bounds.EnvRadius, config, mutationStream),
            OrgRadius = Drift(genome.OrgRadius, bounds.OrgRadius, config, mutationStream),
            SensoryAcuity = Drift(genome.SensoryAcuity, bounds.SensoryAcuity, config, mutationStream),
        }.Clamped(bounds);
    }

    private static double Drift(double value, TraitBounds.Range range, MutationConfig config, Prng mutationStream)
    {
        if (mutationStream.NextDouble() >= config.TraitMutationRate)
        {
            return value;
        }

        double span = range.Max - range.Min;
        double delta = ((mutationStream.NextDouble() * 2.0) - 1.0) * config.TraitMutationDelta * span;
        return value + delta;
    }
}
