using LifeSim.Core.Configuration;

namespace LifeSim.Core.Organisms;

/// <summary>The energy transaction economy: pure functions over genome + config, so they're testable without a live organism.</summary>
public static class Metabolism
{
    public static double BaseMetabolism(Genome genome, MetabolismConfig config) =>
        genome.Size * config.BaseMetabolismPerSize;

    /// <summary>Zero inside the thermal envelope (center ± half-width); scales linearly beyond it.</summary>
    public static double ThermalStress(Genome genome, double tileTemperature, MetabolismConfig config)
    {
        double halfWidth = genome.ThermalWidth / 2.0;
        double deviation = Math.Max(0.0, Math.Abs(tileTemperature - genome.ThermalCenter) - halfWidth);
        return deviation * config.ThermalStressScale;
    }

    /// <summary>Linear on <c>env_radius</c> (static terrain sensing), quadratic on <c>org_radius</c> (moving-agent sensing).</summary>
    public static double SensoryTax(Genome genome, MetabolismConfig config) =>
        (genome.EnvRadius * config.SensoryTaxC1)
        + (genome.OrgRadius * genome.OrgRadius * config.SensoryTaxC2)
        + (genome.SensoryAcuity * config.SensoryTaxC3);

    /// <summary>
    /// Density-dependent crowding cost: energy per neighbour beyond the free
    /// allowance. A continuous carrying-capacity pressure that only bites in crowded areas.
    /// </summary>
    public static double CrowdingTax(int neighbours, MetabolismConfig config)
    {
        int crowded = Math.Max(0, neighbours - config.CrowdingFreeNeighbours);
        return crowded * config.CrowdingCostPerNeighbour;
    }

    /// <summary>
    /// Optional senescence tax: extra metabolism that grows linearly with age once an
    /// organism is older than <see cref="MetabolismConfig.SenescenceOnsetAge"/>. Zero for the young and
    /// zero entirely unless the aging model is enabled by the caller.
    /// </summary>
    public static double SenescenceTax(long age, MetabolismConfig config)
    {
        long agedTicks = Math.Max(0L, age - config.SenescenceOnsetAge);
        return agedTicks * config.SenescenceCostPerTick;
    }

    /// <summary>
    /// Multiplier applied to an organism's self-generated running costs (base upkeep, multicellular
    /// overhead, sensory tax, locomotion) from its evolvable <c>metabolic_efficiency</c>. Ranges from 1
    /// (no frugality) down to <c>1 − MaxMetabolicReduction</c> at maximal frugality — always positive,
    /// so cost asymptotes toward but never reaches zero (the thermodynamic floor).
    /// </summary>
    public static double EfficiencyCostMultiplier(Genome genome, MetabolismConfig config) =>
        1.0 - (config.MaxMetabolicReduction * genome.MetabolicEfficiency);

    /// <summary>
    /// Multiplier applied to grazing yield — the rate–yield trade-off for <c>metabolic_efficiency</c>.
    /// Ranges from 1 (full yield) down to <c>1 − EfficiencyIntakePenalty</c> at maximal frugality, so a
    /// frugal metabolism extracts less usable energy per graze.
    /// </summary>
    public static double EfficiencyYieldMultiplier(Genome genome, MetabolismConfig config) =>
        1.0 - (config.EfficiencyIntakePenalty * genome.MetabolicEfficiency);

    /// <summary>The full base-metabolism equation: base + thermal stress + sensory tax.</summary>
    public static double Total(Genome genome, double tileTemperature, MetabolismConfig config) =>
        BaseMetabolism(genome, config)
        + ThermalStress(genome, tileTemperature, config)
        + SensoryTax(genome, config);

    /// <summary>
    /// Distance × velocity^exponent × biome friction. NOTE: uses <see cref="Math.Pow"/>
    /// for a general exponent; like <c>Prng.NextGaussian</c>, this is a transcendental-determinism
    /// caveat rather than a guaranteed bit-identical cross-platform primitive.
    /// </summary>
    public static double LocomotionTax(double distance, double velocity, double biomeFriction, MovementCombatConfig config) =>
        distance * Math.Pow(velocity, config.LocomotionVelocityExponent) * biomeFriction;
}
