using LifeSim.Core.World;

namespace LifeSim.Core.Configuration;

/// <summary>
/// The typed, versioned configuration block. Every coupled constant
/// lives here rather than in source, so experiments tune behaviour without code changes. Defaults
/// started from Appendix A's illustrative values; Phase 12 calibration then tuned a
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

    /// <summary>Moisture noise layer.</summary>
    public NoiseConfig MoistureNoise { get; init; } = NoiseConfig.Default;

    /// <summary>Temperature noise layer.</summary>
    public NoiseConfig TemperatureNoise { get; init; } = NoiseConfig.Default;

    /// <summary>Genesis organism count.</summary>
    public int InitialPopulation { get; init; } = 200;

    /// <summary>
    /// Aging model, selectable per world at genesis. When enabled, organisms past
    /// <see cref="MetabolismConfig.SenescenceOnsetAge"/> pay a growing metabolic tax so no lineage is
    /// immortal by hoarding energy; on by default. Disable it to leave turnover to famine and predation.
    /// </summary>
    public bool Senescence { get; init; } = true;

    public static SimulationConfig Default => new();
}

/// <summary>Metabolism &amp; sensory-tax coefficients.</summary>
public sealed record MetabolismConfig
{
    public double BaseMetabolismPerSize { get; init; } = 0.05;
    public double SensoryTaxC1 { get; init; } = 0.02;   // linear on env_radius
    public double SensoryTaxC2 { get; init; } = 0.002;  // quadratic on org_radius (Phase 12 calibration; was 0.01)
    public double SensoryTaxC3 { get; init; } = 0.05;   // on sensory_acuity
    public double ThermalStressScale { get; init; } = 0.1;

    /// <summary>
    /// The <c>metabolic_efficiency</c> trait's evolvable payoff and price (the biological rate–yield
    /// trade-off). <see cref="MaxMetabolicReduction"/> is the largest fraction of self-generated running
    /// cost (base upkeep + multicellular overhead + sensory tax + locomotion) that maximal frugality
    /// (efficiency = 1) can shave off — kept below 1 so cost asymptotes toward but never reaches zero
    /// (the thermodynamic floor). <see cref="EfficiencyIntakePenalty"/> is the matching fraction of
    /// grazing yield that maximal frugality gives up, so being efficient means extracting less usable
    /// energy per graze — worthwhile where food is scarce, costly where it is abundant. Set the penalty
    /// to 0 for a no-trade-off, free-lunch efficiency.
    /// </summary>
    public double MaxMetabolicReduction { get; init; } = 0.6;
    public double EfficiencyIntakePenalty { get; init; } = 0.5;

    /// <summary>
    /// Density-dependent crowding cost: extra metabolism per neighbouring organism
    /// in the 3×3 block beyond <see cref="CrowdingFreeNeighbours"/>. A continuous carrying-capacity
    /// pressure — dense clusters starve faster, so overpopulation is self-limiting.
    /// </summary>
    public double CrowdingCostPerNeighbour { get; init; } = 0.5;

    /// <summary>Neighbours tolerated before the crowding cost applies (a kin pair is free).</summary>
    public int CrowdingFreeNeighbours { get; init; } = 1;

    /// <summary>
    /// Age (in ticks) at which senescence begins to add metabolic cost, when the optional aging model
    /// is enabled (<see cref="SimulationConfig.Senescence"/>). Below this age there is
    /// no senescence tax at all.
    /// </summary>
    public long SenescenceOnsetAge { get; init; } = 400;

    /// <summary>Extra metabolism per tick of age beyond <see cref="SenescenceOnsetAge"/> (aging model only).</summary>
    public double SenescenceCostPerTick { get; init; } = 0.02;
}

/// <summary>Movement &amp; combat constants.</summary>
public sealed record MovementCombatConfig
{
    public double LocomotionVelocityExponent { get; init; } = 2.0;
    public double PredationTransferFraction { get; init; } = 0.75;
    public double FailedCombatPenalty { get; init; } = 5.0;
}

/// <summary>Cooperation controls: energy sharing and optional kin-predation deterrence.</summary>
public sealed record CooperationConfig
{
    /// <summary>
    /// Master switch for the whole cooperation feature set, selectable per world at
    /// genesis. When false, Share actions are inert no-ops and the kin-predation penalty is not charged,
    /// so a run can be observed with cooperation entirely absent. The kin-relatedness sensory input
    /// remains available either way — it is cheap information, not cooperation itself.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Fraction of the shared energy the recipient actually receives (&lt; 1 keeps altruism costly).</summary>
    public double ShareEfficiency { get; init; } = 0.8;

    /// <summary>
    /// Sharing is relatedness-scaled: when an organism chooses to Share, it actually
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
/// Multicellularity controls: a body is a single tile-occupant made of
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

    /// <summary>Extra energy ceiling per Store cell above the generalist baseline — how a body "stores more".</summary>
    public double StoreCapacityPerCell { get; init; } = 12.0;

    /// <summary>Coordination/upkeep energy each cell beyond the first costs per tick — the "requires more energy" term (volume, ∝ N).</summary>
    public double CoordinationCostPerCell { get; init; } = 0.15;

    /// <summary>
    /// Division-of-labour efficiency: a body drawing on several <em>distinct</em>
    /// specialist cell types waives up to this fraction of its multicellular overhead (the extra-cell
    /// metabolism + coordination), so a well-differentiated body is far cheaper to run than a lopsided
    /// or generalist one of the same size — the selection pressure toward specialised multicellularity.
    /// </summary>
    public double DivisionOfLabourDiscount { get; init; } = 0.7;

    /// <summary>Number of distinct specialist types (fractions above the ⅙ baseline) for the full <see cref="DivisionOfLabourDiscount"/>; fewer scales it down linearly.</summary>
    public int DivisionOfLabourTarget { get; init; } = 4;

    /// <summary>Surface-exchange coefficient: max energy a body can absorb from grazing per tick is this × N^⅔.</summary>
    public double IntakeSurfaceCoeff { get; init; } = 20.0;

    /// <summary>
    /// Grazing footprint: a larger body physically covers more ground, so when it
    /// grazes it also skims tiles within a reach that grows with cell count (radius ≈ (√cells − 1) ×
    /// this scale, so footprint area ∝ cells) — pulling energy from more of the surface. Total intake
    /// is still capped by <see cref="IntakeSurfaceCoeff"/>×N^⅔, so the footprint mainly lets big bodies
    /// feed on sparse terrain. A single cell grazes only its target tile.
    /// </summary>
    public double GrazingReachScale { get; init; } = 1.0;

    /// <summary>Hard cap on grazing-footprint radius in tiles, so a giant body's per-tick tile scan stays bounded.</summary>
    public int MaxGrazingReach { get; init; } = 3;

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

    /// <summary>A body needs at least this germ fraction to reproduce; below it the body is sterile soma.</summary>
    public double GermReproductionThreshold { get; init; } = 0.05;

    /// <summary>
    /// Neural capacity from body size: a larger body devotes more cells to
    /// processing, so it runs extra recurrent brain-propagation steps per tick — this many per cell
    /// beyond the first — letting signals propagate deeper through the network before it acts (more
    /// efficient decision-making). A single cell always runs exactly one step (the base model).
    /// </summary>
    public double NeuralStepsPerCell { get; init; } = 0.5;

    /// <summary>Hard cap on brain-propagation steps per tick, so a giant body's per-tick compute stays bounded.</summary>
    public int MaxNeuralSteps { get; init; } = 6;

    /// <summary>
    /// Heredity bias toward multicellularity: when a multicellular parent reproduces,
    /// its offspring's cell count is nudged upward by this fraction of the parent's extra cells
    /// (<c>parent_cells − 1</c>). Without it, symmetric trait drift near the unicellular floor erodes
    /// multicellularity back to single cells; with it, the trait is self-reinforcing and the square-cube
    /// economy (not drift) is what caps eventual body size. 0 disables the bias.
    /// </summary>
    public double OffspringGrowthBias { get; init; } = 0.5;
}

/// <summary>Per-biome physics/resource settings.</summary>
public sealed record BiomesConfig
{
    public BiomeSettings Grassland { get; init; } = new() { Friction = 1.0, RegenRate = 0.8, EnergyCap = 20.0, Temperature = 20.0 };
    public BiomeSettings Desert { get; init; } = new() { Friction = 0.8, RegenRate = 0.05, EnergyCap = 5.0, Temperature = 45.0 };
    public BiomeSettings Swamp { get; init; } = new() { Friction = 3.0, RegenRate = 1.5, EnergyCap = 40.0, Temperature = 25.0 };
    // A minimal energy trickle — harsher than the desert, but non-zero — so a cold-adapted, lean
    // lineage (cold thermal_center, small size, sparse senses) can scrape a living on the ice where a
    // grassland generalist would starve, rather than the ice being flatly uninhabitable.
    public BiomeSettings IceSheet { get; init; } = new() { Friction = 1.2, RegenRate = 0.02, EnergyCap = 3.0, Temperature = -15.0 };

    /// <summary>Moisture/temperature noise bands that select a biome from the matrix.</summary>
    public BiomeThresholds Thresholds { get; init; } = new();

    /// <summary>
    /// Within-biome temperature spread in °C: a tile's temperature is its
    /// biome's baseline <see cref="BiomeSettings.Temperature"/> plus the temperature-noise field
    /// scaled by this, so tiles vary around the biome norm rather than all reading identically.
    /// </summary>
    public double TemperatureVariation { get; init; } = 5.0;

    /// <summary>
    /// Radius (in tiles) of the box blur applied to biome baseline temperatures, so sharp biome
    /// borders become temperature *gradients* rather than walls — an organism at a margin feels an
    /// intermediate temperature and can adapt into the neighbouring biome incrementally. 0 restores
    /// hard biome-edged temperatures; interior tiles far from any border are unchanged.
    /// </summary>
    public int TemperatureGradientRadius { get; init; } = 2;

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
/// by the biome matrix: Low/High moisture and Cold/Temperate/Hot temperature.
/// </summary>
public sealed record BiomeThresholds
{
    public double MoistureHighThreshold { get; init; }
    public double TemperatureColdThreshold { get; init; } = -0.33;
    public double TemperatureHotThreshold { get; init; } = 0.33;
}

/// <summary>Reproduction economy.</summary>
public sealed record ReproductionConfig
{
    public double ReproductionBaseCost { get; init; } = 3.0;  // per unit Size (Phase 12 calibration; was 10.0)
    public double OffspringEnergyFraction { get; init; } = 0.5;
    public int ReproductionCooldownTicks { get; init; } = 3;
}

/// <summary>Trait/topology mutation controls.</summary>
public sealed record MutationConfig
{
    public double TraitMutationRate { get; init; } = 0.1;
    public double TraitMutationDelta { get; init; } = 0.05;
    public double WeightMutationRate { get; init; } = 0.8;
    public double WeightMutationPower { get; init; } = 0.5;
    public double ConnectionMutationRate { get; init; } = 0.05;
    public double NodeMutationRate { get; init; } = 0.03;
}

/// <summary>Hard min/max for every mutable trait.</summary>
public sealed record TraitBounds
{
    public Range Size { get; init; } = new(0.5, 10.0);
    public Range SpeedCapacity { get; init; } = new(0.0, 5.0);
    public Range ThermalCenter { get; init; } = new(-20.0, 50.0);
    public Range ThermalWidth { get; init; } = new(2.0, 40.0);
    public Range EnvRadius { get; init; } = new(0.0, 20.0);
    public Range OrgRadius { get; init; } = new(0.0, 20.0);
    public Range SensoryAcuity { get; init; } = new(0.0, 1.0);

    /// <summary>Metabolic frugality: 0 = baseline metabolism, 1 = maximally frugal. Founders start at 0 and evolve up.</summary>
    public Range MetabolicEfficiency { get; init; } = new(0.0, 1.0);

    /// <summary>Generosity bounds: 0 = never donates, 1 = donates all of its energy per Share.</summary>
    public Range ShareFraction { get; init; } = new(0.0, 1.0);

    /// <summary>Body size in cells: 1 = unicellular; the square-cube economy caps the viable maximum well below this hard bound.</summary>
    public Range CellCount { get; init; } = new(1.0, 32.0);

    /// <summary>Specialisation weights: raw propensities, normalised to fractions across the six cell types at use.</summary>
    public Range GermWeight { get; init; } = new(0.0, 1.0);
    public Range FeederWeight { get; init; } = new(0.0, 1.0);
    public Range StoreWeight { get; init; } = new(0.0, 1.0);
    public Range DefenderWeight { get; init; } = new(0.0, 1.0);
    public Range MoverWeight { get; init; } = new(0.0, 1.0);
    public Range SensorWeight { get; init; } = new(0.0, 1.0);

    public sealed record Range(double Min, double Max);
}

/// <summary>Stochastic event tuning.</summary>
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

    /// <summary>Extra energy drained per tick from each organism in a crowded region during a plague.</summary>
    public double PlagueEnergyDrainPerTick { get; init; } = 2.0;

    public double CorpseEnergyFraction { get; init; } = 0.25;
}

/// <summary>Organism naming.</summary>
public sealed record NamingConfig
{
    public string AdjectiveListVersion { get; init; } = "adjectives-1";
    public string NounListVersion { get; init; } = "nouns-1";
    public bool RequireDistinctAdjectives { get; init; }
}
