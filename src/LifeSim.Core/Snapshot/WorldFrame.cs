using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.World;

namespace LifeSim.Core.Snapshot;

/// <summary>
/// Lightweight presentation/transport state. Unlike <see cref="WorldSnapshot"/>, a frame is not
/// replayable: it omits PRNG state, innovation counters, germlines, and every unselected brain. The
/// optional <see cref="DetailOrganism"/> carries one full live brain for the inspector.
/// </summary>
public sealed record WorldFrame
{
    public long Tick { get; init; }
    public string? SnapshotId { get; init; }
    public string? ParentSnapshotId { get; init; }
    public string? BranchId { get; init; }
    public WorldState World { get; init; } = new();
    public SimulationConfig Configuration { get; init; } = SimulationConfig.Default;
    public List<GroundEnergyEntry> GroundEnergy { get; init; } = [];
    public List<OrganismFrame> Organisms { get; init; } = [];
    public OrganismSnapshot? DetailOrganism { get; init; }
    public SimulationMetrics? Metrics { get; init; }
    public List<LineageSnapshot> Lineages { get; init; } = [];
    public List<EnvironmentModifier> EnvironmentModifiers { get; init; } = [];
    public List<EditLogEntry> EditLog { get; init; } = [];

    /// <summary>Adapts the frame to the existing read-only presentation model.</summary>
    public WorldSnapshot ToPresentationSnapshot()
    {
        long detailId = DetailOrganism?.OrganismId ?? -1;
        return new WorldSnapshot
        {
            Tick = Tick,
            SnapshotId = SnapshotId,
            ParentSnapshotId = ParentSnapshotId,
            BranchId = BranchId,
            World = World,
            Configuration = Configuration,
            GroundEnergy = GroundEnergy,
            Organisms = Organisms.Select(o => o.OrganismId == detailId ? DetailOrganism! : o.ToSnapshot()).ToList(),
            Metrics = Metrics,
            Lineages = Lineages,
            EnvironmentModifiers = EnvironmentModifiers,
            EditLog = EditLog,
        };
    }
}

/// <summary>An organism's render/list state without its large live and germline neural networks.</summary>
public sealed record OrganismFrame
{
    public long OrganismId { get; init; }
    public string Name { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public double Energy { get; init; }
    public long Age { get; init; }
    public GenomeSnapshot Genome { get; init; } = new();
    public OrganismAction? LastAction { get; init; }
    public ActionResult LastActionResult { get; init; }
    public long? LastBirthTick { get; init; }
    public long PredationWins { get; init; }
    public long PredationLosses { get; init; }
    public double HelpGiven { get; init; }
    public int BrainNodeCount { get; init; }
    public double Intelligence { get; init; }

    public static OrganismFrame From(Organism organism) => new()
    {
        OrganismId = organism.Id,
        Name = organism.Name,
        X = organism.X,
        Y = organism.Y,
        Energy = organism.Energy,
        Age = organism.Age,
        Genome = GenomeSnapshot.From(organism.Genome),
        LastAction = organism.LastAction,
        LastActionResult = organism.LastActionResult,
        LastBirthTick = organism.LastBirthTick,
        PredationWins = organism.PredationWins,
        PredationLosses = organism.PredationLosses,
        HelpGiven = organism.HelpGiven,
        BrainNodeCount = organism.Brain.Nodes.Count,
        Intelligence = BrainComplexity.Score(organism.Brain),
    };

    public OrganismSnapshot ToSnapshot() => new()
    {
        OrganismId = OrganismId,
        Name = Name,
        X = X,
        Y = Y,
        Energy = Energy,
        Age = Age,
        Genome = Genome,
        Brain = new NeatGenome(),
        LastAction = LastAction,
        LastActionResult = LastActionResult,
        LastBirthTick = LastBirthTick,
        PredationWins = PredationWins,
        PredationLosses = PredationLosses,
        HelpGiven = HelpGiven,
        BrainNodeCount = BrainNodeCount,
        Intelligence = Intelligence,
    };
}
