using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Snapshot;

/// <summary>An organism's dynamic state, inherited genome, and brain, as stored in a snapshot.</summary>
public sealed record OrganismSnapshot
{
    public long OrganismId { get; init; }
    public string Name { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public double Energy { get; init; }
    public long Age { get; init; }
    public GenomeSnapshot Genome { get; init; } = new();

    /// <summary>The NEAT genome; node <c>state</c> must round-trip for the save/reload test.</summary>
    public NeatGenome Brain { get; init; } = new();

    /// <summary>The action selected last tick; null before an organism's first decision.</summary>
    public OrganismAction? LastAction { get; init; }

    /// <summary>The outcome of <see cref="LastAction"/>, fed back in as a sensory input.</summary>
    public ActionResult LastActionResult { get; init; }

    /// <summary>Null if this organism has never reproduced; reconstructs cooldown/readiness without replay.</summary>
    public long? LastBirthTick { get; init; }

    /// <summary>Lifetime successful hunts (kills).</summary>
    public long PredationWins { get; init; }

    /// <summary>Lifetime failed hunts (fought off).</summary>
    public long PredationLosses { get; init; }

    public static OrganismSnapshot From(Organism organism) => new()
    {
        OrganismId = organism.Id,
        Name = organism.Name,
        X = organism.X,
        Y = organism.Y,
        Energy = organism.Energy,
        Age = organism.Age,
        Genome = GenomeSnapshot.From(organism.Genome),
        Brain = organism.Brain,
        LastAction = organism.LastAction,
        LastActionResult = organism.LastActionResult,
        LastBirthTick = organism.LastBirthTick,
        PredationWins = organism.PredationWins,
        PredationLosses = organism.PredationLosses,
    };

    public Organism ToOrganism(double? energyCapacity = null) =>
        new(OrganismId, Genome.ToGenome(), Name, Energy, X, Y, Brain, Age, LastAction, LastActionResult, LastBirthTick, energyCapacity, PredationWins, PredationLosses);
}

/// <summary>The inheritable trait values, as stored in a snapshot.</summary>
public sealed record GenomeSnapshot
{
    public double Size { get; init; }
    public double SpeedCapacity { get; init; }
    public double ThermalCenter { get; init; }
    public double ThermalWidth { get; init; }
    public double EnvRadius { get; init; }
    public double OrgRadius { get; init; }
    public double SensoryAcuity { get; init; }
    public double MetabolicEfficiency { get; init; }
    public double ShareFraction { get; init; }
    public double CellCount { get; init; } = 1.0;
    public double GermWeight { get; init; }
    public double FeederWeight { get; init; }
    public double StoreWeight { get; init; }
    public double DefenderWeight { get; init; }
    public double MoverWeight { get; init; }
    public double SensorWeight { get; init; }

    public static GenomeSnapshot From(Organisms.Genome genome) => new()
    {
        Size = genome.Size,
        SpeedCapacity = genome.SpeedCapacity,
        ThermalCenter = genome.ThermalCenter,
        ThermalWidth = genome.ThermalWidth,
        EnvRadius = genome.EnvRadius,
        OrgRadius = genome.OrgRadius,
        SensoryAcuity = genome.SensoryAcuity,
        MetabolicEfficiency = genome.MetabolicEfficiency,
        ShareFraction = genome.ShareFraction,
        CellCount = genome.CellCount,
        GermWeight = genome.GermWeight,
        FeederWeight = genome.FeederWeight,
        StoreWeight = genome.StoreWeight,
        DefenderWeight = genome.DefenderWeight,
        MoverWeight = genome.MoverWeight,
        SensorWeight = genome.SensorWeight,
    };

    public Organisms.Genome ToGenome() => new()
    {
        Size = Size,
        SpeedCapacity = SpeedCapacity,
        ThermalCenter = ThermalCenter,
        ThermalWidth = ThermalWidth,
        EnvRadius = EnvRadius,
        OrgRadius = OrgRadius,
        SensoryAcuity = SensoryAcuity,
        MetabolicEfficiency = MetabolicEfficiency,
        ShareFraction = ShareFraction,
        CellCount = CellCount,
        GermWeight = GermWeight,
        FeederWeight = FeederWeight,
        StoreWeight = StoreWeight,
        DefenderWeight = DefenderWeight,
        MoverWeight = MoverWeight,
        SensorWeight = SensorWeight,
    };
}
