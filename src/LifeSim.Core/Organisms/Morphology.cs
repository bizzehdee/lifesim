using LifeSim.Core.Configuration;

namespace LifeSim.Core.Organisms;

/// <summary>
/// Pure body-plan math for aggregate multicellularity (lifesim.md §21). A body is a single
/// tile-occupant made of <c>cell_count</c> cells allocated across six jobs; these functions turn a
/// <see cref="Genome"/> + <see cref="MulticellularConfig"/> into the derived quantities the tick loop
/// needs. Every effect is expressed as a bonus for emphasising a type <em>above</em> the ⅙ generalist
/// baseline, so a perfectly generalist 1-cell body is neutral — identical to a plain organism. When
/// multicellularity is disabled, every body collapses to a single generalist cell.
/// </summary>
public static class Morphology
{
    /// <summary>The six specialised jobs a cell can take; the residual generalist share does a little of everything.</summary>
    public readonly record struct CellFractions(
        double Germ, double Feeder, double Store, double Defender, double Mover, double Sensor);

    /// <summary>The ⅙ share of each type in a fully generalist body — the neutral point for every specialisation bonus.</summary>
    public const double GeneralistShare = 1.0 / 6.0;

    /// <summary>Effective cell count: clamped to ≥ 1, and forced to 1 when multicellularity is disabled.</summary>
    public static double CellCount(Genome genome, MulticellularConfig config) =>
        config.Enabled ? Math.Max(1.0, genome.CellCount) : 1.0;

    /// <summary>
    /// The six specialisation weights normalised to fractions summing to 1. A body with no
    /// specialisation weight at all defaults to an even generalist split (⅙ each), so it stays viable.
    /// </summary>
    public static CellFractions Fractions(Genome genome, MulticellularConfig config)
    {
        if (!config.Enabled)
        {
            return Even();
        }

        double germ = Math.Max(0.0, genome.GermWeight);
        double feeder = Math.Max(0.0, genome.FeederWeight);
        double store = Math.Max(0.0, genome.StoreWeight);
        double defender = Math.Max(0.0, genome.DefenderWeight);
        double mover = Math.Max(0.0, genome.MoverWeight);
        double sensor = Math.Max(0.0, genome.SensorWeight);

        double sum = germ + feeder + store + defender + mover + sensor;
        if (sum < 1e-9)
        {
            return Even();
        }

        return new CellFractions(germ / sum, feeder / sum, store / sum, defender / sum, mover / sum, sensor / sum);
    }

    /// <summary>Total body mass = cells × per-cell size; drives metabolism and combat (a 1-cell body = its Size, as before).</summary>
    public static double Mass(Genome genome, MulticellularConfig config) =>
        CellCount(genome, config) * genome.Size;

    /// <summary>Energy ceiling: a base capacity plus a bonus for every Store cell above the generalist baseline (lifesim.md §21).</summary>
    public static double Capacity(Genome genome, MulticellularConfig config)
    {
        if (!config.Enabled)
        {
            return config.BaseCapacity;
        }

        double storeCells = Excess(Fractions(genome, config).Store) * CellCount(genome, config);
        return config.BaseCapacity + (storeCells * config.StoreCapacityPerCell);
    }

    /// <summary>
    /// Coordination energy per tick for every cell beyond the first (lifesim.md §21). This is the
    /// multicellular-specific overhead; the raw per-cell metabolic upkeep (∝ N) is applied at the
    /// metabolism call site by scaling base metabolism by <see cref="CellCount"/>. Together they are
    /// the volume cost that — against surface-limited intake — makes the square-cube law bite.
    /// </summary>
    public static double CoordinationCost(Genome genome, MulticellularConfig config) =>
        config.Enabled ? (CellCount(genome, config) - 1.0) * config.CoordinationCostPerCell : 0.0;

    /// <summary>Max energy absorbable from grazing per tick — surface exchange, ∝ N^⅔ (the square-cube ceiling on intake).</summary>
    public static double MaxGrazingIntake(Genome genome, MulticellularConfig config) =>
        config.Enabled
            ? config.IntakeSurfaceCoeff * Math.Pow(CellCount(genome, config), 2.0 / 3.0)
            : double.PositiveInfinity;

    /// <summary>Grazing yield multiplier from Feeder emphasis (1 for a generalist body).</summary>
    public static double FeedMultiplier(Genome genome, MulticellularConfig config) =>
        1.0 + (Excess(Fractions(genome, config).Feeder) * config.FeederYieldBonus);

    /// <summary>Effective combat mass: body mass boosted by Defender emphasis.</summary>
    public static double CombatMass(Genome genome, MulticellularConfig config) =>
        Mass(genome, config) * (1.0 + (Excess(Fractions(genome, config).Defender) * config.DefenderCombatBonus));

    /// <summary>Thermal-stress multiplier: Defender emphasis insulates the body (≤ 1).</summary>
    public static double ThermalStressFactor(Genome genome, MulticellularConfig config) =>
        1.0 - (Excess(Fractions(genome, config).Defender) * config.DefenderThermalResist);

    /// <summary>Locomotion-tax multiplier: Mover emphasis makes the body cheaper to move (≤ 1).</summary>
    public static double LocomotionFactor(Genome genome, MulticellularConfig config) =>
        1.0 - (Excess(Fractions(genome, config).Mover) * config.MoverEfficiency);

    /// <summary>Effective sensory acuity: Sensor emphasis sharpens perception (less injected noise), capped at 1.</summary>
    public static double EffectiveAcuity(Genome genome, MulticellularConfig config) =>
        config.Enabled
            ? Math.Min(1.0, genome.SensoryAcuity + (Excess(Fractions(genome, config).Sensor) * config.SensorAcuityBonus))
            : genome.SensoryAcuity;

    /// <summary>Whether the body carries enough germ cells to reproduce; below the threshold it is sterile soma (lifesim.md §21).</summary>
    public static bool CanReproduce(Genome genome, MulticellularConfig config) =>
        !config.Enabled || Fractions(genome, config).Germ >= config.GermReproductionThreshold;

    /// <summary>The specialisation share above the generalist baseline (0 for generalist or de-emphasised types); the driver of every bonus.</summary>
    private static double Excess(double fraction) => Math.Max(0.0, fraction - GeneralistShare);

    private static CellFractions Even() =>
        new(GeneralistShare, GeneralistShare, GeneralistShare, GeneralistShare, GeneralistShare, GeneralistShare);
}
