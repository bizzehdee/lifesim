using LifeSim.Core.Configuration;

namespace LifeSim.Core.Organisms;

/// <summary>The energy transaction economy (lifesim.md §3): pure functions over genome + config, so they're testable without a live organism.</summary>
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
    /// Density-dependent crowding cost (lifesim.md §3, §6): energy per neighbour beyond the free
    /// allowance. A continuous carrying-capacity pressure that only bites in crowded areas.
    /// </summary>
    public static double CrowdingTax(int neighbours, MetabolismConfig config)
    {
        int crowded = Math.Max(0, neighbours - config.CrowdingFreeNeighbours);
        return crowded * config.CrowdingCostPerNeighbour;
    }

    /// <summary>
    /// Optional senescence tax (lifesim.md §17): extra metabolism that grows linearly with age once an
    /// organism is older than <see cref="MetabolismConfig.SenescenceOnsetAge"/>. Zero for the young and
    /// zero entirely unless the aging model is enabled by the caller.
    /// </summary>
    public static double SenescenceTax(long age, MetabolismConfig config)
    {
        long agedTicks = Math.Max(0L, age - config.SenescenceOnsetAge);
        return agedTicks * config.SenescenceCostPerTick;
    }

    /// <summary>The full base-metabolism equation: base + thermal stress + sensory tax (lifesim.md §3).</summary>
    public static double Total(Genome genome, double tileTemperature, MetabolismConfig config) =>
        BaseMetabolism(genome, config)
        + ThermalStress(genome, tileTemperature, config)
        + SensoryTax(genome, config);

    /// <summary>
    /// Distance × velocity^exponent × biome friction (lifesim.md §3). NOTE: uses <see cref="Math.Pow"/>
    /// for a general exponent; like <c>Prng.NextGaussian</c>, this is a transcendental-determinism
    /// caveat (lifesim.md §9) rather than a guaranteed bit-identical cross-platform primitive.
    /// </summary>
    public static double LocomotionTax(double distance, double velocity, double biomeFriction, MovementCombatConfig config) =>
        distance * Math.Pow(velocity, config.LocomotionVelocityExponent) * biomeFriction;
}
