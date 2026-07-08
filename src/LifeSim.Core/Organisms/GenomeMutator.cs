using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;

namespace LifeSim.Core.Organisms;

/// <summary>
/// Applies bounded, inheritable trait drift to an offspring genome. Each trait
/// mutates independently with probability <see cref="MutationConfig.TraitMutationRate"/>; the
/// perturbation is a uniform delta scaled to a fraction (<see cref="MutationConfig.TraitMutationDelta"/>)
/// of that trait's own bound span, so a single config value drifts every trait proportionally
/// regardless of its absolute range. All draws come from the mutation PRNG stream,
/// in a fixed trait order, and the result is hard-clamped to <see cref="TraitBounds"/>.
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
        // happen in a fixed, reproducible trait sequence.
        return new Genome
        {
            Size = Drift(genome.Size, bounds.Size, config, mutationStream),
            SpeedCapacity = Drift(genome.SpeedCapacity, bounds.SpeedCapacity, config, mutationStream),
            ThermalCenter = Drift(genome.ThermalCenter, bounds.ThermalCenter, config, mutationStream),
            ThermalWidth = Drift(genome.ThermalWidth, bounds.ThermalWidth, config, mutationStream),
            EnvRadius = Drift(genome.EnvRadius, bounds.EnvRadius, config, mutationStream),
            OrgRadius = Drift(genome.OrgRadius, bounds.OrgRadius, config, mutationStream),
            SensoryAcuity = Drift(genome.SensoryAcuity, bounds.SensoryAcuity, config, mutationStream),
            MetabolicEfficiency = Drift(genome.MetabolicEfficiency, bounds.MetabolicEfficiency, config, mutationStream),
            Armour = Drift(genome.Armour, bounds.Armour, config, mutationStream),
            Evasion = Drift(genome.Evasion, bounds.Evasion, config, mutationStream),
            Toxicity = Drift(genome.Toxicity, bounds.Toxicity, config, mutationStream),
            Plasticity = Drift(genome.Plasticity, bounds.Plasticity, config, mutationStream),
            ShareFraction = Drift(genome.ShareFraction, bounds.ShareFraction, config, mutationStream),
            CellCount = Drift(genome.CellCount, bounds.CellCount, config, mutationStream),
            GermWeight = Drift(genome.GermWeight, bounds.GermWeight, config, mutationStream),
            FeederWeight = Drift(genome.FeederWeight, bounds.FeederWeight, config, mutationStream),
            StoreWeight = Drift(genome.StoreWeight, bounds.StoreWeight, config, mutationStream),
            DefenderWeight = Drift(genome.DefenderWeight, bounds.DefenderWeight, config, mutationStream),
            MoverWeight = Drift(genome.MoverWeight, bounds.MoverWeight, config, mutationStream),
            SensorWeight = Drift(genome.SensorWeight, bounds.SensorWeight, config, mutationStream),
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
