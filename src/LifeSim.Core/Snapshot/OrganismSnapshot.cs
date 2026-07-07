using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Snapshot;

/// <summary>An organism's dynamic state, inherited genome, and brain, as stored in a snapshot (lifesim.md §12).</summary>
public sealed record OrganismSnapshot
{
    public long OrganismId { get; init; }
    public string Name { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public double Energy { get; init; }
    public long Age { get; init; }
    public GenomeSnapshot Genome { get; init; } = new();

    /// <summary>The NEAT genome (lifesim.md §4, §12); node <c>state</c> must round-trip for the save/reload test.</summary>
    public NeatGenome Brain { get; init; } = new();

    /// <summary>The action selected last tick (lifesim.md §12, §18); null before an organism's first decision.</summary>
    public OrganismAction? LastAction { get; init; }

    /// <summary>The outcome of <see cref="LastAction"/> (lifesim.md §12, §13), fed back in as a sensory input.</summary>
    public ActionResult LastActionResult { get; init; }

    /// <summary>Null if this organism has never reproduced; reconstructs cooldown/readiness without replay (lifesim.md §12).</summary>
    public long? LastBirthTick { get; init; }

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
    };

    public Organism ToOrganism() =>
        new(OrganismId, Genome.ToGenome(), Name, Energy, X, Y, Brain, Age, LastAction, LastActionResult, LastBirthTick);
}

/// <summary>The inheritable trait values (lifesim.md §3, §8), as stored in a snapshot.</summary>
public sealed record GenomeSnapshot
{
    public double Size { get; init; }
    public double SpeedCapacity { get; init; }
    public double ThermalCenter { get; init; }
    public double ThermalWidth { get; init; }
    public double EnvRadius { get; init; }
    public double OrgRadius { get; init; }
    public double SensoryAcuity { get; init; }
    public double ShareFraction { get; init; }

    public static GenomeSnapshot From(Organisms.Genome genome) => new()
    {
        Size = genome.Size,
        SpeedCapacity = genome.SpeedCapacity,
        ThermalCenter = genome.ThermalCenter,
        ThermalWidth = genome.ThermalWidth,
        EnvRadius = genome.EnvRadius,
        OrgRadius = genome.OrgRadius,
        SensoryAcuity = genome.SensoryAcuity,
        ShareFraction = genome.ShareFraction,
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
        ShareFraction = ShareFraction,
    };
}
