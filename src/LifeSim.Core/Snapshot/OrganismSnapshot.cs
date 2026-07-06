using LifeSim.Core.Organisms;

namespace LifeSim.Core.Snapshot;

/// <summary>An organism's dynamic state and inherited genome, as stored in a snapshot (lifesim.md §12).</summary>
public sealed record OrganismSnapshot
{
    public long OrganismId { get; init; }
    public string Name { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public double Energy { get; init; }
    public long Age { get; init; }
    public GenomeSnapshot Genome { get; init; } = new();

    /// <summary>The action selected last tick (lifesim.md §12, §18); null before an organism's first decision.</summary>
    public OrganismAction? LastAction { get; init; }

    public static OrganismSnapshot From(Organism organism) => new()
    {
        OrganismId = organism.Id,
        Name = organism.Name,
        X = organism.X,
        Y = organism.Y,
        Energy = organism.Energy,
        Age = organism.Age,
        Genome = GenomeSnapshot.From(organism.Genome),
        LastAction = organism.LastAction,
    };

    public Organism ToOrganism() =>
        new(OrganismId, Genome.ToGenome(), Name, Energy, X, Y, Age, LastAction);
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

    public static GenomeSnapshot From(Organisms.Genome genome) => new()
    {
        Size = genome.Size,
        SpeedCapacity = genome.SpeedCapacity,
        ThermalCenter = genome.ThermalCenter,
        ThermalWidth = genome.ThermalWidth,
        EnvRadius = genome.EnvRadius,
        OrgRadius = genome.OrgRadius,
        SensoryAcuity = genome.SensoryAcuity,
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
    };
}
