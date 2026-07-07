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

    /// <summary>Optional aging model; off in v1 (lifesim.md §17).</summary>
    public bool Senescence { get; init; }

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
}

/// <summary>Movement &amp; combat constants (lifesim.md §3, §5, Appendix A).</summary>
public sealed record MovementCombatConfig
{
    public double LocomotionVelocityExponent { get; init; } = 2.0;
    public double PredationTransferFraction { get; init; } = 0.75;
    public double FailedCombatPenalty { get; init; } = 5.0;
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
