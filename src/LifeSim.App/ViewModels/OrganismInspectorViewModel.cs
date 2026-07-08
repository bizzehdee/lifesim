using LifeSim.App.Presentation;
using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.App.ViewModels;

/// <summary>A genome trait shown against its hard bounds; <see cref="Fraction"/> ∈ [0,1] drives a bar.</summary>
public sealed record TraitReading(string Name, double Value, double Min, double Max)
{
    public double Fraction => Max <= Min ? 0.0 : Math.Clamp((Value - Min) / (Max - Min), 0.0, 1.0);
}

/// <summary>The metabolism equation broken out for one organism.</summary>
public sealed record EconomyBreakdown(double Base, double ThermalStress, double SensoryTax, double LastMovementCost)
{
    public double Total => Base + ThermalStress + SensoryTax + LastMovementCost;
}

/// <summary>One action's current softmax probability.</summary>
public sealed record ActionProbability(OrganismAction Action, double Probability);

/// <summary>One cell type's share of a multicellular body: its cell count and fraction of the body.</summary>
public sealed record CellTypeReading(string Type, double Cells, double Fraction);

/// <summary>
/// Every stat behind one organism, all sourced from its snapshot record plus the
/// world config/terrain — identity &amp; lineage, physical state, genome-vs-bounds, the per-tick
/// economy breakdown, behaviour + softmax distribution, and the brain graph. Built fresh per
/// selection; a pure projection of state, so live and loaded frames inspect identically.
/// </summary>
public sealed class OrganismInspectorViewModel : ViewModelBase
{
    private OrganismInspectorViewModel(OrganismSnapshot organism)
    {
        Organism = organism;
    }

    public OrganismSnapshot Organism { get; }

    // Identity & lineage.
    public string Name => Organism.Name;
    public long OrganismId => Organism.OrganismId;
    public long LineageId { get; private init; }

    /// <summary>The brain "type" this organism's lineage was founded from (e.g. "Selfish", "Generic").</summary>
    public string FoundingType { get; private init; } = "Generic";
    public long? ParentId { get; private init; }
    public string? ParentName { get; private init; }
    public bool ParentAlive { get; private init; }

    /// <summary>The sexual-reproduction co-parent, if this organism was born from two parents; else null.</summary>
    public long? SecondParentId { get; private init; }
    public string? SecondParentName { get; private init; }
    public int GenerationDepth { get; private init; }
    public long BirthTick { get; private init; }
    public long Age => Organism.Age;

    /// <summary>How many offspring this organism has produced (lineage records whose parent is this one).</summary>
    public long ChildCount { get; private init; }

    // Predation record (lifetime).
    public long PredationAttempts => Organism.PredationWins + Organism.PredationLosses;
    public long PredationWins => Organism.PredationWins;
    public long PredationLosses => Organism.PredationLosses;

    /// <summary>Lifetime relatedness-weighted energy donated — this organism's indirect-fitness (kin-help) contribution.</summary>
    public double HelpGiven => Organism.HelpGiven;

    // Physical state.
    public int X => Organism.X;
    public int Y => Organism.Y;

    /// <summary>Grid coordinate as "(x, y)" — a single binding source so both components render.</summary>
    public string Position => $"({X}, {Y})";
    public Biome Biome { get; private init; }
    public double Energy => Organism.Energy;
    public double EnergyCeiling => Core.Organisms.Organism.EnergyCeiling;
    public bool ReproductiveReady { get; private init; }
    public long CooldownRemaining { get; private init; }

    /// <summary>
    /// Plain-language reproduction mode, derived from fertility and the <c>sexuality</c> trait:
    /// "Sterile" (soma — can't reproduce at all), "Asexual" (sexuality 0 — always clones), "Sexual"
    /// (sexuality 1 — always seeks a mate, cloning only when none is adjacent), or "Mixed" (in between —
    /// attempts a mate part of the time, otherwise clones).
    /// </summary>
    public string ReproductionMode { get; private init; } = "Asexual";

    // Genome vs bounds.
    public IReadOnlyList<TraitReading> Traits { get; private init; } = [];

    // Body plan (multicellularity).
    public bool Multicellular { get; private init; }
    public double CellCount { get; private init; } = 1.0;
    public bool Fertile { get; private init; } = true;

    /// <summary>Recurrent brain-propagation steps this body runs per tick — its neural depth.</summary>
    public int BrainSteps { get; private init; } = 1;
    public IReadOnlyList<CellTypeReading> CellComposition { get; private init; } = [];

    // Per-tick economy.
    public EconomyBreakdown Economy { get; private init; } = new(0, 0, 0, 0);

    // Behaviour.
    public OrganismAction? LastAction => Organism.LastAction;
    public ActionResult LastActionResult => Organism.LastActionResult;
    public IReadOnlyList<ActionProbability> ActionProbabilities { get; private init; } = [];

    // Brain.
    public string NetworkType => Organism.Brain.NetworkType;
    public int NodeCount => Organism.Brain.Nodes.Count;
    public int HiddenNodeCount => Organism.Brain.Nodes.Count(n => n.Type == NodeType.Hidden);
    public int ConnectionCount => Organism.Brain.Connections.Count;
    public int EnabledConnectionCount => Organism.Brain.Connections.Count(c => c.Enabled);
    public NeatGraph BrainGraph { get; private init; } = new([], []);

    /// <summary>Effective feed-forward depth of the evolved network (layers from input to output).</summary>
    public int NetworkDepth => BrainGraph.Depth;

    /// <summary>Builds the inspector for organism <paramref name="organismId"/>, or null if it isn't in the snapshot.</summary>
    public static OrganismInspectorViewModel? Create(WorldSnapshot snapshot, long organismId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        OrganismSnapshot? organism = snapshot.Organisms.FirstOrDefault(o => o.OrganismId == organismId);
        if (organism is null)
        {
            return null;
        }

        SimulationConfig config = snapshot.Configuration;
        var terrain = new TerrainSampler(snapshot.World.Seed, config);
        var environment = new EnvironmentState(snapshot.EnvironmentModifiers);
        Genome genome = organism.Genome.ToGenome();
        TraitBounds bounds = config.TraitBounds;

        double tileTemperature = terrain.TemperatureCelsiusAt(organism.X, organism.Y) + environment.TemperatureOffset;
        double friction = config.Biomes.For(terrain.BiomeAt(organism.X, organism.Y)).Friction;
        double movementCost = IsMove(organism.LastAction)
            ? Metabolism.LocomotionTax(1.0, genome.SpeedCapacity, friction, config.MovementCombat)
            : 0.0;

        LineageSnapshot? lineage = snapshot.Lineages.FirstOrDefault(l => l.OrganismId == organismId);
        var livingIds = snapshot.Organisms.Select(o => o.OrganismId).ToHashSet();
        long childCount = snapshot.Lineages.Count(l => l.ParentId == organismId);
        string? parentName = lineage?.ParentId is { } parentId
            ? snapshot.Organisms.FirstOrDefault(o => o.OrganismId == parentId)?.Name
            : null;
        string? secondParentName = lineage?.SecondParentId is { } secondParentId
            ? snapshot.Organisms.FirstOrDefault(o => o.OrganismId == secondParentId)?.Name
            : null;

        bool reproReady = organism.Energy >= config.Reproduction.ReproductionBaseCost * genome.Size
            && (organism.LastBirthTick is not { } birth || snapshot.Tick - birth >= config.Reproduction.ReproductionCooldownTicks);
        long cooldownRemaining = organism.LastBirthTick is { } lastBirth
            ? Math.Max(0, config.Reproduction.ReproductionCooldownTicks - (snapshot.Tick - lastBirth))
            : 0;

        double[] probabilities = NeatBrain.ActionProbabilities(organism.Brain);

        MulticellularConfig mc = config.Multicellular;
        double cells = Morphology.CellCount(genome, mc);
        Morphology.CellFractions fr = Morphology.Fractions(genome, mc);

        return new OrganismInspectorViewModel(organism)
        {
            Multicellular = mc.Enabled,
            CellCount = cells,
            Fertile = Morphology.CanReproduce(genome, mc),
            ReproductionMode = DescribeReproductionMode(Morphology.CanReproduce(genome, mc), genome.Sexuality),
            BrainSteps = Morphology.BrainSteps(genome, mc),
            CellComposition =
            [
                new CellTypeReading("Germ", fr.Germ * cells, fr.Germ),
                new CellTypeReading("Feeder", fr.Feeder * cells, fr.Feeder),
                new CellTypeReading("Store", fr.Store * cells, fr.Store),
                new CellTypeReading("Defender", fr.Defender * cells, fr.Defender),
                new CellTypeReading("Mover", fr.Mover * cells, fr.Mover),
                new CellTypeReading("Sensor", fr.Sensor * cells, fr.Sensor),
            ],
            LineageId = lineage?.LineageId ?? organismId,
            FoundingType = lineage?.FoundingType ?? "Generic",
            ParentId = lineage?.ParentId,
            ParentName = parentName,
            SecondParentId = lineage?.SecondParentId,
            SecondParentName = secondParentName,
            GenerationDepth = lineage?.GenerationDepth ?? 0,
            BirthTick = lineage?.BirthTick ?? 0,
            ParentAlive = lineage?.ParentId is { } parent && livingIds.Contains(parent),
            ChildCount = childCount,
            Biome = terrain.BiomeAt(organism.X, organism.Y),
            ReproductiveReady = reproReady,
            CooldownRemaining = cooldownRemaining,
            Traits =
            [
                new TraitReading("Size", genome.Size, bounds.Size.Min, bounds.Size.Max),
                new TraitReading("Speed Capacity", genome.SpeedCapacity, bounds.SpeedCapacity.Min, bounds.SpeedCapacity.Max),
                new TraitReading("Thermal Centre", genome.ThermalCenter, bounds.ThermalCenter.Min, bounds.ThermalCenter.Max),
                new TraitReading("Thermal Width", genome.ThermalWidth, bounds.ThermalWidth.Min, bounds.ThermalWidth.Max),
                new TraitReading("Env Radius", genome.EnvRadius, bounds.EnvRadius.Min, bounds.EnvRadius.Max),
                new TraitReading("Org Radius", genome.OrgRadius, bounds.OrgRadius.Min, bounds.OrgRadius.Max),
                new TraitReading("Sensory Acuity", genome.SensoryAcuity, bounds.SensoryAcuity.Min, bounds.SensoryAcuity.Max),
                new TraitReading("Metabolic Efficiency", genome.MetabolicEfficiency, bounds.MetabolicEfficiency.Min, bounds.MetabolicEfficiency.Max),
                new TraitReading("Armour", genome.Armour, bounds.Armour.Min, bounds.Armour.Max),
                new TraitReading("Evasion", genome.Evasion, bounds.Evasion.Min, bounds.Evasion.Max),
                new TraitReading("Toxicity", genome.Toxicity, bounds.Toxicity.Min, bounds.Toxicity.Max),
                new TraitReading("Plasticity", genome.Plasticity, bounds.Plasticity.Min, bounds.Plasticity.Max),
                new TraitReading("Learning Decay", genome.LearningDecay, bounds.LearningDecay.Min, bounds.LearningDecay.Max),
                new TraitReading("Sexuality", genome.Sexuality, bounds.Sexuality.Min, bounds.Sexuality.Max),
                new TraitReading("Generosity", genome.ShareFraction, bounds.ShareFraction.Min, bounds.ShareFraction.Max),
            ],
            Economy = new EconomyBreakdown(
                Metabolism.BaseMetabolism(genome, config.Metabolism),
                Metabolism.ThermalStress(genome, tileTemperature, config.Metabolism),
                Metabolism.SensoryTax(genome, config.Metabolism),
                movementCost),
            ActionProbabilities = probabilities
                .Select((p, i) => new ActionProbability((OrganismAction)i, p))
                .ToList(),
            BrainGraph = NeatGraphLayout.Build(organism.Brain),
        };
    }

    private static bool IsMove(OrganismAction? action) => action is OrganismAction.MoveNorth
        or OrganismAction.MoveSouth or OrganismAction.MoveEast or OrganismAction.MoveWest;

    private static string DescribeReproductionMode(bool fertile, double sexuality) => (fertile, sexuality) switch
    {
        (false, _) => "Sterile (soma)",
        (_, <= 0.0) => "Asexual (clones)",
        (_, >= 1.0) => "Sexual (seeks a mate; clones if none)",
        _ => $"Mixed (~{sexuality:P0} sexual)",
    };
}
