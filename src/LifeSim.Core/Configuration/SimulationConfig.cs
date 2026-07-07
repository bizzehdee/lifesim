using LifeSim.Core.World;

namespace LifeSim.Core.Configuration;

/// <summary>
/// The typed, versioned configuration block (lifesim.md §12, Appendix A). Every coupled constant
/// lives here rather than in source, so experiments tune behaviour without code changes. Defaults
/// started from Appendix A's illustrative values; Phase 12 calibration (lifesim.md §15) then tuned a
/// few (reproduction cost, org-radius sensory tax, grassland regen) so a grassland population is
/// sustainable under random genesis brains without runaway growth.
/// </summary>
public sealed record SimulationConfig
{
    public MetabolismConfig Metabolism { get; init; } = new();
    public MovementCombatConfig MovementCombat { get; init; } = new();
    public CooperationConfig Cooperation { get; init; } = new();
    public MulticellularConfig Multicellular { get; init; } = new();
    public BiomesConfig Biomes { get; init; } = new();
    public ReproductionConfig Reproduction { get; init; } = new();
    public MutationConfig Mutation { get; init; } = new();
    public TraitBounds TraitBounds { get; init; } = new();
    public EventsConfig Events { get; init; } = new();
    public NamingConfig Naming { get; init; } = new();

    /// <summary>Moisture noise layer (lifesim.md §2).</summary>
    public NoiseConfig MoistureNoise { get; init; } = NoiseConfig.Default;

    /// <summary>Temperature noise layer (lifesim.md §2).</summary>
    public NoiseConfig TemperatureNoise { get; init; } = NoiseConfig.Default;

    /// <summary>Genesis organism count (lifesim.md §17).</summary>
    public int InitialPopulation { get; init; } = 200;

    /// <summary>
    /// Aging model (lifesim.md §17), selectable per world at genesis. When enabled, organisms past
    /// <see cref="MetabolismConfig.SenescenceOnsetAge"/> pay a growing metabolic tax so no lineage is
    /// immortal by hoarding energy; on by default. Disable it to leave turnover to famine and predation.
    /// </summary>
    public bool Senescence { get; init; } = true;

    public static SimulationConfig Default => new();
}

/// <summary>Metabolism &amp; sensory-tax coefficients (lifesim.md §3, Appendix A).</summary>
public sealed record MetabolismConfig
{
    public double BaseMetabolismPerSize { get; init; } = 0.05;
    public double SensoryTaxC1 { get; init; } = 0.02;   // linear on env_radius
    public double SensoryTaxC2 { get; init; } = 0.002;  // quadratic on org_radius (Phase 12 calibration; was 0.01)
    public double SensoryTaxC3 { get; init; } = 0.05;   // on sensory_acuity
    public double ThermalStressScale { get; init; } = 0.1;

    /// <summary>
    /// Density-dependent crowding cost (lifesim.md §3, §6): extra metabolism per neighbouring organism
    /// in the 3×3 block beyond <see cref="CrowdingFreeNeighbours"/>. A continuous carrying-capacity
    /// pressure — dense clusters starve faster, so overpopulation is self-limiting.
    /// </summary>
    public double CrowdingCostPerNeighbour { get; init; } = 0.5;

    /// <summary>Neighbours tolerated before the crowding cost applies (a kin pair is free).</summary>
    public int CrowdingFreeNeighbours { get; init; } = 1;

    /// <summary>
    /// Age (in ticks) at which senescence begins to add metabolic cost, when the optional aging model
    /// is enabled (<see cref="SimulationConfig.Senescence"/>, lifesim.md §17). Below this age there is
    /// no senescence tax at all.
    /// </summary>
    public long SenescenceOnsetAge { get; init; } = 400;

    /// <summary>Extra metabolism per tick of age beyond <see cref="SenescenceOnsetAge"/> (aging model only).</summary>
    public double SenescenceCostPerTick { get; init; } = 0.02;
}

/// <summary>Movement &amp; combat constants (lifesim.md §3, §5, Appendix A).</summary>
public sealed record MovementCombatConfig
{
    public double LocomotionVelocityExponent { get; init; } = 2.0;
    public double PredationTransferFraction { get; init; } = 0.75;
    public double FailedCombatPenalty { get; init; } = 5.0;
}

/// <summary>Cooperation controls (lifesim.md §20): energy sharing and optional kin-predation deterrence.</summary>
public sealed record CooperationConfig
{
    /// <summary>
    /// Master switch for the whole cooperation feature set (lifesim.md §20), selectable per world at
    /// genesis. When false, Share actions are inert no-ops and the kin-predation penalty is not charged,
    /// so a run can be observed with cooperation entirely absent. The kin-relatedness sensory input
    /// remains available either way — it is cheap information, not cooperation itself.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Genesis generosity: the <see cref="Genome.ShareFraction"/> every founder starts with. From here
    /// the per-organism generosity trait evolves freely (§20), so this is only the starting point, not
    /// a global constant — the actual amount donated by any Share is the donor's own evolved trait.
    /// </summary>
    public double ShareFraction { get; init; } = 0.25;

    /// <summary>Fraction of the shared energy the recipient actually receives (&lt; 1 keeps altruism costly).</summary>
    public double ShareEfficiency { get; init; } = 0.8;

    /// <summary>
    /// Sharing is relatedness-scaled (lifesim.md §20): when an organism chooses to Share, it actually
    /// donates with probability <c>floor + (ceiling − floor) · relatedness(donor, recipient)</c>. The
    /// ceiling &lt; 1 means even kin aren't certain; the floor &gt; 0 means strangers are unlikely but possible.
    /// </summary>
    public double ShareProbabilityFloor { get; init; } = 0.05;

    public double ShareProbabilityCeiling { get; init; } = 0.9;

    /// <summary>Relatedness at/above which the closest organism counts as kin (for the deterrent and metrics).</summary>
    public double KinRelatednessThreshold { get; init; } = 0.9;

    /// <summary>Extra energy an attacker pays for killing kin — a tunable anti-cannibalism nudge; 0 disables it.</summary>
    public double KinPredationPenalty { get; init; }
}

/// <summary>
/// Multicellularity controls (lifesim.md §21): a body is a single tile-occupant made of
/// <c>cell_count</c> cells, each specialised toward one of six jobs. Bigger bodies pay more upkeep
/// (volume, ∝ N) but exchange energy only through their surface (∝ N^⅔), so the square-cube law caps
/// viable size. Every specialisation effect is a <em>bonus for emphasising a type above the ⅙
/// generalist baseline</em>, so a perfectly generalist 1-cell body behaves like a plain organism.
/// </summary>
public sealed record MulticellularConfig
{
    /// <summary>Master switch, selectable per world at genesis. When off, every body is a single generalist cell (current model).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Energy ceiling of a 1-cell / storage-free body (matches <see cref="Organisms.Organism.EnergyCeiling"/>).</summary>
    public double BaseCapacity { get; init; } = 100.0;

    /// <summary>Extra energy ceiling per Store cell above the generalist baseline — how a body "stores more" (lifesim.md §21).</summary>
    public double StoreCapacityPerCell { get; init; } = 12.0;

    /// <summary>Coordination/upkeep energy each cell beyond the first costs per tick — the "requires more energy" term (volume, ∝ N).</summary>
    public double CoordinationCostPerCell { get; init; } = 0.15;

    /// <summary>Surface-exchange coefficient: max energy a body can absorb from grazing per tick is this × N^⅔ (lifesim.md §21).</summary>
    public double IntakeSurfaceCoeff { get; init; } = 20.0;

    /// <summary>Feeder emphasis (above baseline) multiplies grazing yield up to 1 + this.</summary>
    public double FeederYieldBonus { get; init; } = 1.0;

    /// <summary>Defender emphasis multiplies effective combat mass up to 1 + this.</summary>
    public double DefenderCombatBonus { get; init; } = 1.0;

    /// <summary>Defender emphasis reduces thermal stress by up to this fraction.</summary>
    public double DefenderThermalResist { get; init; } = 0.5;

    /// <summary>Mover emphasis reduces locomotion tax by up to this fraction.</summary>
    public double MoverEfficiency { get; init; } = 0.5;

    /// <summary>Sensor emphasis adds up to this to effective sensory acuity (less perception noise).</summary>
    public double SensorAcuityBonus { get; init; } = 0.6;

    /// <summary>A body needs at least this germ fraction to reproduce; below it the body is sterile soma (lifesim.md §21).</summary>
    public double GermReproductionThreshold { get; init; } = 0.05;
}

/// <summary>Per-biome physics/resource settings (lifesim.md §2, Appendix A).</summary>
public sealed record BiomesConfig
{
    public BiomeSettings Grassland { get; init; } = new() { Friction = 1.0, RegenRate = 0.8, EnergyCap = 20.0, Temperature = 20.0 };
    public BiomeSettings Desert { get; init; } = new() { Friction = 0.8, RegenRate = 0.05, EnergyCap = 5.0, Temperature = 45.0 };
    public BiomeSettings Swamp { get; init; } = new() { Friction = 3.0, RegenRate = 1.5, EnergyCap = 40.0, Temperature = 25.0 };
    public BiomeSettings IceSheet { get; init; } = new() { Friction = 1.2, RegenRate = 0.0, EnergyCap = 0.0, Temperature = -15.0 };

    /// <summary>Moisture/temperature noise bands that select a biome from the matrix (lifesim.md §2).</summary>
    public BiomeThresholds Thresholds { get; init; } = new();

    /// <summary>
    /// Within-biome temperature spread in °C (lifesim.md §2, §3): a tile's temperature is its
    /// biome's baseline <see cref="BiomeSettings.Temperature"/> plus the temperature-noise field
    /// scaled by this, so tiles vary around the biome norm rather than all reading identically.
    /// </summary>
    public double TemperatureVariation { get; init; } = 5.0;

    public BiomeSettings For(Biome biome) => biome switch
    {
        Biome.Grassland => Grassland,
        Biome.Desert => Desert,
        Biome.Swamp => Swamp,
        Biome.IceSheet => IceSheet,
        _ => throw new ArgumentOutOfRangeException(nameof(biome), biome, null),
    };
}

public sealed record BiomeSettings
{
    public double Friction { get; init; } = 1.0;
    public double RegenRate { get; init; } = 0.5;
    public double EnergyCap { get; init; } = 20.0;
    public double Temperature { get; init; } = 20.0;
}

/// <summary>
/// Splits the moisture and temperature noise fields (each roughly [-1, 1]) into the bands used
/// by the biome matrix (lifesim.md §2): Low/High moisture and Cold/Temperate/Hot temperature.
/// </summary>
public sealed record BiomeThresholds
{
    public double MoistureHighThreshold { get; init; }
    public double TemperatureColdThreshold { get; init; } = -0.33;
    public double TemperatureHotThreshold { get; init; } = 0.33;
}

/// <summary>Reproduction economy (lifesim.md §8, §11, §17, Appendix A).</summary>
public sealed record ReproductionConfig
{
    public double ReproductionBaseCost { get; init; } = 3.0;  // per unit Size (Phase 12 calibration; was 10.0)
    public double OffspringEnergyFraction { get; init; } = 0.5;
    public int ReproductionCooldownTicks { get; init; } = 3;
}

/// <summary>Trait/topology mutation controls (lifesim.md §4, §8, Appendix A).</summary>
public sealed record MutationConfig
{
    public double TraitMutationRate { get; init; } = 0.1;
    public double TraitMutationDelta { get; init; } = 0.05;
    public double WeightMutationRate { get; init; } = 0.8;
    public double WeightMutationPower { get; init; } = 0.5;
    public double ConnectionMutationRate { get; init; } = 0.05;
    public double NodeMutationRate { get; init; } = 0.03;
}

/// <summary>Hard min/max for every mutable trait (lifesim.md §3, §8, Appendix A).</summary>
public sealed record TraitBounds
{
    public Range Size { get; init; } = new(0.5, 10.0);
    public Range SpeedCapacity { get; init; } = new(0.0, 5.0);
    public Range ThermalCenter { get; init; } = new(-20.0, 50.0);
    public Range ThermalWidth { get; init; } = new(2.0, 40.0);
    public Range EnvRadius { get; init; } = new(0.0, 20.0);
    public Range OrgRadius { get; init; } = new(0.0, 20.0);
    public Range SensoryAcuity { get; init; } = new(0.0, 1.0);

    /// <summary>Generosity bounds (lifesim.md §20): 0 = never donates, 1 = donates all of its energy per Share.</summary>
    public Range ShareFraction { get; init; } = new(0.0, 1.0);

    /// <summary>Body size in cells (lifesim.md §21): 1 = unicellular; the square-cube economy caps the viable maximum well below this hard bound.</summary>
    public Range CellCount { get; init; } = new(1.0, 32.0);

    /// <summary>Specialisation weights (lifesim.md §21): raw propensities, normalised to fractions across the six cell types at use.</summary>
    public Range GermWeight { get; init; } = new(0.0, 1.0);
    public Range FeederWeight { get; init; } = new(0.0, 1.0);
    public Range StoreWeight { get; init; } = new(0.0, 1.0);
    public Range DefenderWeight { get; init; } = new(0.0, 1.0);
    public Range MoverWeight { get; init; } = new(0.0, 1.0);
    public Range SensorWeight { get; init; } = new(0.0, 1.0);

    public sealed record Range(double Min, double Max);
}

/// <summary>Stochastic event tuning (lifesim.md §6, Appendix A).</summary>
public sealed record EventsConfig
{
    public double BlightProbability { get; init; } = 0.001;
    public double PlagueProbability { get; init; } = 0.001;
    public double TemperatureAnomalyProbability { get; init; } = 0.001;
    public int BlightDurationTicks { get; init; } = 50;
    public int PlagueDurationTicks { get; init; } = 40;
    public int TemperatureAnomalyDurationTicks { get; init; } = 60;
    public double TemperatureAnomalyMagnitude { get; init; } = 20.0;
    public int PlagueDensityThreshold { get; init; } = 6;

    /// <summary>Extra energy drained per tick from each organism in a crowded region during a plague (lifesim.md §6).</summary>
    public double PlagueEnergyDrainPerTick { get; init; } = 2.0;

    public double CorpseEnergyFraction { get; init; } = 0.25;
}

/// <summary>Organism naming (lifesim.md §19, Appendix A).</summary>
public sealed record NamingConfig
{
    public string AdjectiveListVersion { get; init; } = "adjectives-1";
    public string NounListVersion { get; init; } = "nouns-1";
    public bool RequireDistinctAdjectives { get; init; }
}
