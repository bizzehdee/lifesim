using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Events;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.Sensing;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Simulation;

/// <summary>
/// The tick-loop aggregate root: terrain, ground energy, PRNG streams, active event modifiers, and
/// the live organism index, advanced one phased tick at a time in the authoritative order
/// (lifesim.md §7). Offspring inherit their parent's genome and brain with mutation applied in the
/// Birth Commit phase (lifesim.md §8).
/// </summary>
public sealed class SimulationWorld
{
    private readonly TerrainSampler _terrain;
    private readonly GroundEnergyGrid _groundEnergy;
    private readonly PrngStreams _prngStreams;
    private readonly OrganismIdAllocator _idAllocator;
    private readonly InnovationIdAllocator _innovationIdAllocator;
    private readonly EnvironmentState _environment;
    private readonly SensoryInputBuilder _sensoryInputBuilder;
    private readonly SortedDictionary<long, Organism> _organisms = new();
    private readonly Dictionary<(int X, int Y), long> _occupancy = new();

    /// <summary>Number of bins in each per-trait distribution histogram (lifesim.md §14).</summary>
    private const int HistogramBucketCount = 10;

    /// <summary>Metrics for the most recently completed tick (lifesim.md §7, §14); rebuilt every Metrics phase.</summary>
    private SimulationMetrics _metrics = new();

    /// <summary>Provenance of UI interventions (lifesim.md §16), preserved across resume so it never silently drops.</summary>
    private List<EditLogEntry> _editLog = [];

    // Branch provenance (lifesim.md §16); carried verbatim across resume, never minted by the engine.
    private string? _snapshotId;
    private string? _parentSnapshotId;
    private string? _branchId;

    /// <summary>Ancestry records for every organism that has ever lived — never removed, unlike <see cref="_organisms"/> (lifesim.md §8, §14).</summary>
    private readonly SortedDictionary<long, LineageEntry> _lineageRecords = new();

    public WorldState World { get; }

    public SimulationConfig Config { get; }

    public long Tick { get; private set; }

    /// <summary>
    /// Max threads for the per-organism brain forward pass (lifesim.md §7). Purely an execution knob:
    /// the forward pass is a pure function with no shared writes and no PRNG, so results are
    /// byte-identical for any value ≥ 1 (the single softmax roll stays sequential in id order). Not
    /// part of the snapshot; default 1 (serial). Clamped to ≥ 1 on set.
    /// </summary>
    public int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => _maxDegreeOfParallelism = Math.Max(1, value);
    }

    private int _maxDegreeOfParallelism = 1;

    /// <summary>Set once population reaches zero; the engine halts and never auto-reseeds (lifesim.md §17).</summary>
    public bool Extinct { get; private set; }

    /// <summary>Living organisms in ascending id order (lifesim.md §7, §9).</summary>
    public IReadOnlyDictionary<long, Organism> Organisms => _organisms;

    /// <summary>Ancestry records for every organism that has ever lived, ascending by id.</summary>
    public IReadOnlyDictionary<long, LineageEntry> LineageRecords => _lineageRecords;

    /// <summary>The most recently computed tick metrics (lifesim.md §14) — the same object <see cref="ToSnapshot"/> carries.</summary>
    public SimulationMetrics Metrics => _metrics;

    private SimulationWorld(
        WorldState world, SimulationConfig config, TerrainSampler terrain, GroundEnergyGrid groundEnergy,
        PrngStreams prngStreams, OrganismIdAllocator idAllocator, InnovationIdAllocator innovationIdAllocator,
        EnvironmentState environment)
    {
        World = world;
        Config = config;
        _terrain = terrain;
        _groundEnergy = groundEnergy;
        _prngStreams = prngStreams;
        _idAllocator = idAllocator;
        _innovationIdAllocator = innovationIdAllocator;
        _environment = environment;
        _sensoryInputBuilder = new SensoryInputBuilder(terrain, groundEnergy, config, world);
    }

    /// <summary>
    /// Builds Tick 0 (lifesim.md §17): scatters <see cref="SimulationConfig.InitialPopulation"/>
    /// mid-range-genome organisms across Grassland tiles via the genesis PRNG stream.
    /// </summary>
    public static SimulationWorld CreateGenesis(WorldState world, SimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(config);

        var terrain = new TerrainSampler(world.Seed, config);
        var groundEnergy = new GroundEnergyGrid(terrain, config);
        var prngStreams = PrngStreams.FromSeed(world.Seed);
        var idAllocator = new OrganismIdAllocator(0);
        var innovationIdAllocator = new InnovationIdAllocator(NeatTopology.ReservedInnovationIdCount);
        var environment = new EnvironmentState();

        var simWorld = new SimulationWorld(
            world, config, terrain, groundEnergy, prngStreams, idAllocator, innovationIdAllocator, environment);
        simWorld.ScatterGenesisPopulation();
        simWorld.RefreshExtinction();
        simWorld._metrics = simWorld.BuildMetrics(new TickCounters());
        return simWorld;
    }

    /// <summary>Rehydrates a world from a snapshot, restoring PRNG streams, ground energy, every organism, and lineage history.</summary>
    public static SimulationWorld FromSnapshot(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var terrain = new TerrainSampler(snapshot.World.Seed, snapshot.Configuration);
        var groundEnergy = GroundEnergyGrid.FromState(terrain, snapshot.Configuration, snapshot.GroundEnergy);
        var prngStreams = PrngStreams.FromState(snapshot.PrngStreams);
        var idAllocator = new OrganismIdAllocator(snapshot.EvolutionBookkeeping.NextOrganismId);
        var innovationIdAllocator = new InnovationIdAllocator(snapshot.EvolutionBookkeeping.NextInnovationId);
        var environment = new EnvironmentState(snapshot.EnvironmentModifiers);

        var simWorld = new SimulationWorld(
            snapshot.World, snapshot.Configuration, terrain, groundEnergy, prngStreams, idAllocator,
            innovationIdAllocator, environment)
        {
            Tick = snapshot.Tick,
            Extinct = snapshot.Metrics?.Extinct ?? false,
        };

        foreach (OrganismSnapshot entry in snapshot.Organisms)
        {
            // Capacity is a deterministic function of genome + config (lifesim.md §21), recomputed on
            // load rather than stored — so save/reload stays byte-identical.
            Organism organism = entry.ToOrganism(Morphology.Capacity(entry.Genome.ToGenome(), snapshot.Configuration.Multicellular));
            simWorld._organisms[organism.Id] = organism;
            simWorld._occupancy[(organism.X, organism.Y)] = organism.Id;
        }

        foreach (LineageSnapshot entry in snapshot.Lineages)
        {
            LineageEntry lineage = entry.ToEntry();
            simWorld._lineageRecords[lineage.OrganismId] = lineage;
        }

        // Restore the saved metrics so an immediate re-snapshot round-trips exactly; the per-tick
        // flow counters (births/deaths/…) belong to the pre-save tick and are overwritten by the
        // next Advance's Metrics phase. Older snapshots without a metrics block get a fresh build.
        simWorld._metrics = snapshot.Metrics ?? simWorld.BuildMetrics(new TickCounters());
        simWorld._editLog = [.. snapshot.EditLog];
        simWorld._snapshotId = snapshot.SnapshotId;
        simWorld._parentSnapshotId = snapshot.ParentSnapshotId;
        simWorld._branchId = snapshot.BranchId;
        return simWorld;
    }

    public WorldSnapshot ToSnapshot() => new()
    {
        Tick = Tick,
        SnapshotId = _snapshotId,
        ParentSnapshotId = _parentSnapshotId,
        BranchId = _branchId,
        World = World,
        Configuration = Config,
        PrngStreams = _prngStreams.CaptureState(),
        EvolutionBookkeeping = new EvolutionBookkeeping
        {
            // Advanced by structural brain mutations in the Birth Commit phase (lifesim.md §4, §8);
            // seeded past the fixed genesis topology's reserved range so the two never collide.
            NextInnovationId = _innovationIdAllocator.NextId,
            NextOrganismId = _idAllocator.NextId,
        },
        GroundEnergy = _groundEnergy.CaptureState(),
        Organisms = _organisms.Values.Select(OrganismSnapshot.From).ToList(),
        Lineages = _lineageRecords.Values.Select(LineageSnapshot.From).ToList(),
        Metrics = _metrics,
        EnvironmentModifiers = _environment.Modifiers.ToList(),
        EditLog = _editLog.ToList(),
    };

    /// <summary>Advances exactly one tick through the authoritative phase order (lifesim.md §7).</summary>
    public void Advance()
    {
        if (Extinct)
        {
            throw new InvalidOperationException("Cannot advance an extinct (halted) world (lifesim.md §17).");
        }

        // The tick this Advance() call is producing (Tick itself only increments at the very end,
        // in phase 9) — used consistently for birth_tick/death_tick/last_birth_tick bookkeeping.
        long currentTick = Tick + 1;

        // 1. Environment Phase: age/expire active event modifiers, then roll for new stochastic
        //    events against the events stream (lifesim.md §6, §7). Newly-triggered events are live
        //    for the rest of this same tick — the Sensing phase below already sees them.
        _environment.RunEnvironmentPhase(_prngStreams[PrngStream.Events], Config.Events, currentTick);

        // 2. Sensing Phase: every organism's input vector is built from world state as it stands
        //    at the start of the tick — positions/energy are still whatever the previous tick's
        //    Intent Resolution left them, since this phase runs in full before any organism's
        //    decision is even made, let alone resolved (lifesim.md §7).
        Prng sensoryNoise = _prngStreams[PrngStream.SensoryNoise];
        double globalStress = _environment.GlobalStress;
        double temperatureOffset = _environment.TemperatureOffset;
        var sensoryInputs = new Dictionary<long, double[]>(_organisms.Count);
        foreach (long id in _organisms.Keys)
        {
            sensoryInputs[id] = _sensoryInputBuilder.Build(
                _organisms[id], _organisms, currentTick, sensoryNoise, globalStress, temperatureOffset);
        }

        // 3. Decision Phase. Per-organism NEAT evaluation is independent of every other organism
        //    (each reads only its own cached inputs and its own prior brain state). The expensive
        //    forward pass is pure — no PRNG, no shared writes — so it runs across up to
        //    MaxDegreeOfParallelism threads (lifesim.md §7). The single softmax roll then draws from
        //    the shared behavior stream strictly in ascending organism-id order, so the stream is
        //    consumed identically regardless of the parallelism — results are byte-identical for any
        //    thread count (lifesim.md §9).
        Prng behavior = _prngStreams[PrngStream.Behavior];
        long[] decisionIds = _organisms.Keys.ToArray();
        var propagations = new NeatPropagation[decisionIds.Length];

        if (_maxDegreeOfParallelism > 1 && decisionIds.Length > 1)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = _maxDegreeOfParallelism };
            Parallel.For(0, decisionIds.Length, options, i =>
                propagations[i] = NeatBrain.Propagate(_organisms[decisionIds[i]].Brain, sensoryInputs[decisionIds[i]]));
        }
        else
        {
            for (int i = 0; i < decisionIds.Length; i++)
            {
                propagations[i] = NeatBrain.Propagate(_organisms[decisionIds[i]].Brain, sensoryInputs[decisionIds[i]]);
            }
        }

        var actions = new Dictionary<long, OrganismAction>(_organisms.Count);
        for (int i = 0; i < decisionIds.Length; i++)
        {
            long id = decisionIds[i];
            Organism organism = _organisms[id];
            OrganismAction action = NeatBrain.SelectAction(propagations[i].Probabilities, behavior);
            organism.UpdateBrain(propagations[i].Genome);
            organism.RecordAction(action);
            actions[id] = action;
        }

        // 4. Intent Resolution Phase: movement, harvest (grazing/predation), and reproduction
        //    validity/tile-claiming all resolve here, in a single ascending-id pass, since they
        //    share occupancy state and must interleave in strict organism-id priority order
        //    (lifesim.md §7, §10). Offspring are only *reserved* here (tile claimed, parent
        //    charged, id allocated) — actual insertion into the live index happens in the Birth
        //    Commit phase below.
        var counters = new TickCounters();
        var distanceTraveled = new Dictionary<long, double>(_organisms.Count);
        var pendingBirths = new List<PendingBirth>();
        foreach (long id in _organisms.Keys)
        {
            Organism organism = _organisms[id];
            if (!organism.IsAlive)
            {
                // Killed by an earlier (lower-id) organism's predation this same tick; a corpse
                // doesn't get to act.
                distanceTraveled[id] = 0.0;
                continue;
            }

            (double distance, ActionResult result) = ResolveIntent(organism, actions[id], currentTick, behavior, pendingBirths, counters);
            distanceTraveled[id] = distance;
            organism.RecordActionResult(result);
        }

        // 5. Metabolism Phase. Base + thermal stress + sensory tax + movement, plus (lifesim.md §7)
        //    any active-event drains: a climatic anomaly shifts the effective tile temperature that
        //    thermal stress is measured against, and a density plague drains organisms crowded
        //    above the configured threshold.
        double temperatureShift = _environment.TemperatureOffset;
        bool plagueActive = _environment.PlagueActive;
        var energyBeforeMetabolism = new Dictionary<long, double>(_organisms.Count);
        foreach (long id in _organisms.Keys)
        {
            Organism organism = _organisms[id];
            energyBeforeMetabolism[id] = organism.Energy;

            double tileTemperature = _terrain.TemperatureCelsiusAt(organism.X, organism.Y) + temperatureShift;
            double friction = Config.Biomes.For(_terrain.BiomeAt(organism.X, organism.Y)).Friction;
            int localDensity = LocalOrganismDensity(organism); // 3×3 including self

            // Body economy (lifesim.md §21): one cell's base metabolism plus the multicellular overhead
            // for the rest — extra-cell upkeep (volume, ∝ N) + coordination, discounted by division of
            // labour so a well-differentiated body is far cheaper than a lopsided one. Defender cells
            // insulate against thermal stress and Mover cells cut locomotion tax. A generalist 1-cell
            // body reduces to the plain per-organism cost.
            Genome g = organism.Genome;
            MulticellularConfig mc = Config.Multicellular;
            double perCellBase = Metabolism.BaseMetabolism(g, Config.Metabolism);
            double cost = perCellBase
                + Morphology.MulticellularOverhead(g, perCellBase, mc)
                + (Metabolism.ThermalStress(g, tileTemperature, Config.Metabolism) * Morphology.ThermalStressFactor(g, mc))
                + Metabolism.SensoryTax(g, Config.Metabolism)
                + (Metabolism.LocomotionTax(distanceTraveled[id], g.SpeedCapacity, friction, Config.MovementCombat) * Morphology.LocomotionFactor(g, mc))
                + Metabolism.CrowdingTax(localDensity - 1, Config.Metabolism); // density-dependent overpopulation cost (lifesim.md §3, §6)

            if (Config.Senescence)
            {
                cost += Metabolism.SenescenceTax(organism.Age, Config.Metabolism); // optional aging model (lifesim.md §17)
            }

            if (plagueActive && localDensity >= Config.Events.PlagueDensityThreshold)
            {
                cost += Config.Events.PlagueEnergyDrainPerTick;
            }

            organism.SpendEnergy(cost);
            organism.Tick();
        }

        // 6. Death & Transfer Phase. Predation transfer already happened in Intent Resolution;
        //    non-predation deaths (starvation/thermal stress) deposit corpse energy here, based on
        //    the energy they had *before* the fatal Metabolism deduction — a predation victim is
        //    already at exactly 0 by this point, so it correctly contributes no corpse energy
        //    (lifesim.md §11, §17).
        foreach (long id in _organisms.Keys.ToArray())
        {
            Organism organism = _organisms[id];
            if (organism.IsAlive)
            {
                continue;
            }

            double corpseEnergy = energyBeforeMetabolism[id] * Config.Events.CorpseEnergyFraction;
            if (corpseEnergy > 0.0)
            {
                _groundEnergy.Deposit(organism.X, organism.Y, corpseEnergy);
            }

            _lineageRecords[id].RecordDeath(currentTick, organism.Genome);
            _organisms.Remove(id);
            _occupancy.Remove((organism.X, organism.Y));
            counters.Deaths++;
        }

        // 7. Resource Regeneration Phase — suspended entirely while a Resource Blight is active
        //    (lifesim.md §6), which is what forces populations off grazing and toward predation.
        if (!_environment.BlightActive)
        {
            _groundEnergy.RegenerateTick();
        }

        // 8. Mutation & Birth Commit Phase. Trait and brain mutation are applied here, in ascending
        //    offspring-id order, so the mutation stream and the innovation-id counter both advance
        //    deterministically (lifesim.md §4, §8, §9). pendingBirths is already ascending by
        //    offspring id (ids are allocated monotonically as births are reserved), but sort
        //    explicitly to make the contract independent of that.
        Prng mutation = _prngStreams[PrngStream.Mutation];
        foreach (PendingBirth birth in pendingBirths.OrderBy(b => b.OffspringId))
        {
            Genome offspringGenome = GenomeMutator.Mutate(birth.Genome, Config.Mutation, Config.TraitBounds, mutation);

            // Multicellular parents bias their offspring toward more cells (lifesim.md §21), so the
            // trait is self-reinforcing; the square-cube economy still caps the viable size. Applied
            // after mutation and re-clamped; draws no randomness, so replay stays deterministic (§9).
            double biasedCells = Morphology.BiasedOffspringCellCount(
                offspringGenome.CellCount, birth.Genome.CellCount, Config.Multicellular);
            offspringGenome = (offspringGenome with { CellCount = biasedCells }).Clamped(Config.TraitBounds);

            NeatGenome offspringBrain = NeatMutator.Mutate(birth.Brain, Config.Mutation, mutation, _innovationIdAllocator);
            Organism offspring = OrganismFactory.Create(
                birth.OffspringId, offspringGenome, Config.Naming, birth.OffspringEnergy, birth.X, birth.Y, offspringBrain,
                Morphology.Capacity(offspringGenome, Config.Multicellular));
            _organisms[offspring.Id] = offspring;
            _lineageRecords[offspring.Id] = new LineageEntry(
                offspring.Id, birth.ParentId, birth.LineageId, birth.BirthTick, birth.GenerationDepth, offspring.Genome);
        }

        counters.Births = pendingBirths.Count;

        // 9. Metrics & Snapshot Phase (lifesim.md §7, §14).
        Tick++;
        RefreshExtinction();
        _metrics = BuildMetrics(counters);
    }

    private (double Distance, ActionResult Result) ResolveIntent(
        Organism organism, OrganismAction action, long currentTick, Prng behaviorStream,
        List<PendingBirth> pendingBirths, TickCounters counters)
    {
        switch (action)
        {
            case OrganismAction.MoveNorth:
            case OrganismAction.MoveSouth:
            case OrganismAction.MoveEast:
            case OrganismAction.MoveWest:
                (int Dx, int Dy) direction = action switch
                {
                    OrganismAction.MoveNorth => (0, -1),
                    OrganismAction.MoveSouth => (0, 1),
                    OrganismAction.MoveEast => (1, 0),
                    _ => (-1, 0), // MoveWest
                };
                int traveled = ResolveMove(organism, direction.Dx, direction.Dy);
                return (traveled, traveled > 0 ? ActionResult.Success : ActionResult.Blocked);

            case OrganismAction.HarvestSelf:
                return (0.0, ResolveHarvest(organism, 0, 0, behaviorStream, counters));
            case OrganismAction.HarvestNorth:
                return (0.0, ResolveHarvest(organism, 0, -1, behaviorStream, counters));
            case OrganismAction.HarvestSouth:
                return (0.0, ResolveHarvest(organism, 0, 1, behaviorStream, counters));
            case OrganismAction.HarvestEast:
                return (0.0, ResolveHarvest(organism, 1, 0, behaviorStream, counters));
            case OrganismAction.HarvestWest:
                return (0.0, ResolveHarvest(organism, -1, 0, behaviorStream, counters));

            case OrganismAction.Reproduce:
                return (0.0, ResolveReproduce(organism, currentTick, pendingBirths));

            case OrganismAction.ShareNorth:
                return (0.0, ResolveShare(organism, 0, -1, behaviorStream, counters));
            case OrganismAction.ShareSouth:
                return (0.0, ResolveShare(organism, 0, 1, behaviorStream, counters));
            case OrganismAction.ShareEast:
                return (0.0, ResolveShare(organism, 1, 0, behaviorStream, counters));
            case OrganismAction.ShareWest:
                return (0.0, ResolveShare(organism, -1, 0, behaviorStream, counters));

            case OrganismAction.Idle:
                return (0.0, ActionResult.Success);

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    /// <summary>
    /// Multi-tile movement: steps tile-by-tile, stopping at the first off-grid or occupied tile,
    /// so only the distance actually travelled is paid for (lifesim.md §10, §17).
    /// </summary>
    private int ResolveMove(Organism organism, int dx, int dy)
    {
        int maxSteps = (int)Math.Floor(organism.Genome.SpeedCapacity);
        int x = organism.X;
        int y = organism.Y;
        int traveled = 0;

        for (int step = 0; step < maxSteps; step++)
        {
            int nextX = x + dx;
            int nextY = y + dy;

            if (nextX < 0 || nextX >= World.Width || nextY < 0 || nextY >= World.Height)
            {
                break;
            }

            if (_occupancy.ContainsKey((nextX, nextY)))
            {
                break;
            }

            x = nextX;
            y = nextY;
            traveled++;
        }

        if (traveled > 0)
        {
            _occupancy.Remove((organism.X, organism.Y));
            organism.MoveTo(x, y);
            _occupancy[(x, y)] = organism.Id;
        }

        return traveled;
    }

    /// <summary>
    /// The universal Harvest action (lifesim.md §5, §10): grazes ambient ground energy on an
    /// empty target tile, or triggers predatory combat if the target is occupied. Off-grid targets
    /// are a no-op. Outcomes are tallied into <paramref name="counters"/> for metrics (lifesim.md §14):
    /// a kill/failure counts as predation, a graze that gained/gained-nothing as successful/failed
    /// grazing (an off-grid or empty-tile harvest is a failed graze).
    /// </summary>
    private ActionResult ResolveHarvest(Organism organism, int dx, int dy, Prng behaviorStream, TickCounters counters)
    {
        int x = organism.X + dx;
        int y = organism.Y + dy;

        if (x < 0 || x >= World.Width || y < 0 || y >= World.Height)
        {
            counters.FailedGrazing++;
            return ActionResult.NoOp;
        }

        // Combat only against a *live* organism. A tile can also be held in _occupancy by an
        // offspring reserved earlier this same tick that hasn't been materialized into the organism
        // index until Birth Commit (lifesim.md §7) — there's no one physically there to fight yet,
        // so that falls through to ambient grazing rather than a phantom combat.
        if (_occupancy.TryGetValue((x, y), out long targetId) && targetId != organism.Id
            && _organisms.TryGetValue(targetId, out Organism? victim))
        {
            // Combat scales with effective body mass (cells × size), boosted by Defender cells (lifesim.md §21).
            double killProbability = Combat.KillProbability(
                Morphology.CombatMass(organism.Genome, Config.Multicellular),
                Morphology.CombatMass(victim.Genome, Config.Multicellular));
            bool killed = behaviorStream.NextDouble() < killProbability;

            if (killed)
            {
                double victimEnergy = victim.SpendEnergy(victim.Energy);
                organism.AddEnergy(victimEnergy * Config.MovementCombat.PredationTransferFraction);
                counters.SuccessfulPredation++;

                // Kin cannibalism (lifesim.md §20): counted for analytics, and optionally penalised
                // (default off) as a tunable anti-cannibalism deterrent.
                if (Kinship.Relatedness(organism.Genome, victim.Genome, Config.TraitBounds) >= Config.Cooperation.KinRelatednessThreshold)
                {
                    counters.KinPredation++;
                    if (Config.Cooperation.Enabled)
                    {
                        organism.SpendEnergy(Config.Cooperation.KinPredationPenalty);
                    }
                }

                return ActionResult.Killed;
            }

            organism.SpendEnergy(Config.MovementCombat.FailedCombatPenalty);
            counters.FailedPredation++;
            return ActionResult.Failed;
        }

        // Ambient grazing (lifesim.md §21): surface exchange caps how much ground a body can process
        // per tick at ∝ N^⅔ (the square-cube ceiling), while Feeder cells raise how much usable energy
        // it extracts from what it processes. A generalist 1-cell body grazes exactly as before.
        double throughput = Math.Min(_groundEnergy.EnergyAt(x, y), Morphology.MaxGrazingIntake(organism.Genome, Config.Multicellular));
        double drained = _groundEnergy.Drain(x, y, throughput);
        organism.AddEnergy(drained * Morphology.FeedMultiplier(organism.Genome, Config.Multicellular));
        if (drained > 0.0)
        {
            counters.SuccessfulGrazing++;
        }
        else
        {
            counters.FailedGrazing++;
        }

        return ActionResult.Success;
    }

    /// <summary>
    /// The Share action (lifesim.md §20): donate a fraction of this organism's energy to the live
    /// organism on the adjacent target tile, credited at <c>share_efficiency</c> (the lost remainder
    /// is what keeps altruism genuinely costly). Sharing is relatedness-scaled — once chosen, it
    /// actually goes through with probability <c>floor + (ceiling − floor)·relatedness</c> (kin: likely
    /// but not certain; strangers: unlikely but possible), rolled against the behaviour stream (§9).
    /// Off-grid, empty, or not-yet-materialized-offspring targets are a no-op; recipient gain is
    /// clamped to the ceiling and surplus is lost.
    /// </summary>
    private ActionResult ResolveShare(Organism organism, int dx, int dy, Prng behaviorStream, TickCounters counters)
    {
        if (!Config.Cooperation.Enabled)
        {
            return ActionResult.NoOp; // cooperation disabled for this world (lifesim.md §20)
        }

        int x = organism.X + dx;
        int y = organism.Y + dy;

        if (x < 0 || x >= World.Width || y < 0 || y >= World.Height
            || !_occupancy.TryGetValue((x, y), out long targetId) || targetId == organism.Id
            || !_organisms.TryGetValue(targetId, out Organism? recipient))
        {
            counters.FailedShare++;
            return ActionResult.NoOp;
        }

        double relatedness = Kinship.Relatedness(organism.Genome, recipient.Genome, Config.TraitBounds);
        CooperationConfig coop = Config.Cooperation;
        double shareProbability = coop.ShareProbabilityFloor
            + ((coop.ShareProbabilityCeiling - coop.ShareProbabilityFloor) * relatedness);
        if (behaviorStream.NextDouble() >= shareProbability)
        {
            counters.FailedShare++;
            return ActionResult.Failed; // cooperation declined (scaled by relatedness)
        }

        double donated = organism.SpendEnergy(organism.Energy * organism.Genome.ShareFraction);
        recipient.AddEnergy(donated * coop.ShareEfficiency);
        counters.SuccessfulShare++;
        counters.EnergyShared += donated;
        return ActionResult.Success;
    }

    /// <summary>Asexual reproduction gating and offspring reservation (lifesim.md §8, §17, §21).</summary>
    private ActionResult ResolveReproduce(Organism organism, long currentTick, List<PendingBirth> pendingBirths)
    {
        // A body with too few germ cells is sterile soma — it can support the body but not reproduce (lifesim.md §21).
        if (!Morphology.CanReproduce(organism.Genome, Config.Multicellular))
        {
            return ActionResult.Failed;
        }

        // Cost scales with body mass, but division of labour sheds the size penalty (lifesim.md §21),
        // so a well-differentiated body reproduces almost as cheaply as a single cell.
        double cost = Config.Reproduction.ReproductionBaseCost * Morphology.ReproductionMass(organism.Genome, Config.Multicellular);
        if (organism.Energy < cost)
        {
            return ActionResult.Failed;
        }

        if (organism.LastBirthTick is not null
            && currentTick - organism.LastBirthTick.Value < Config.Reproduction.ReproductionCooldownTicks)
        {
            return ActionResult.Failed;
        }

        // Deterministic tile priority: N, S, E, W (matches the Move action ordering).
        (int X, int Y)? freeTile = null;
        foreach ((int ddx, int ddy) in new (int, int)[] { (0, -1), (0, 1), (1, 0), (-1, 0) })
        {
            int x = organism.X + ddx;
            int y = organism.Y + ddy;
            if (x < 0 || x >= World.Width || y < 0 || y >= World.Height || _occupancy.ContainsKey((x, y)))
            {
                continue;
            }

            freeTile = (x, y);
            break;
        }

        if (freeTile is null)
        {
            return ActionResult.Failed;
        }

        organism.SpendEnergy(cost);
        organism.RecordBirth(currentTick);

        double offspringEnergy = cost * Config.Reproduction.OffspringEnergyFraction;
        LineageEntry parentLineage = _lineageRecords[organism.Id];
        long offspringId = _idAllocator.Allocate();

        _occupancy[freeTile.Value] = offspringId;
        pendingBirths.Add(new PendingBirth(
            offspringId, organism.Id, organism.Genome, ResetBrainState(organism.Brain), offspringEnergy,
            freeTile.Value.X, freeTile.Value.Y, currentTick, parentLineage.LineageId, parentLineage.GenerationDepth + 1));

        return ActionResult.Success;
    }

    /// <summary>Node state is dynamic organism state, initialized to zero at birth (lifesim.md §4, §12) — topology/weights are inherited unchanged.</summary>
    private static NeatGenome ResetBrainState(NeatGenome brain) =>
        brain with { Nodes = brain.Nodes.Select(n => n with { State = 0.0 }).ToList() };

    private void ScatterGenesisPopulation()
    {
        Prng genesis = _prngStreams[PrngStream.Genesis];
        // Founders start at the configured genesis generosity (lifesim.md §20); the trait evolves from there.
        Genome genome = Genome.MidRange(Config.TraitBounds) with { ShareFraction = Config.Cooperation.ShareFraction };

        int maxAttempts = Math.Max(10_000, World.Width * World.Height * 4);

        for (int i = 0; i < Config.InitialPopulation; i++)
        {
            bool placed = false;
            for (int attempt = 0; attempt < maxAttempts && !placed; attempt++)
            {
                int x = genesis.NextInt(World.Width);
                int y = genesis.NextInt(World.Height);

                if (_occupancy.ContainsKey((x, y)) || _terrain.BiomeAt(x, y) != Biome.Grassland)
                {
                    continue;
                }

                long id = _idAllocator.Allocate();
                NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(genesis);
                Organism organism = OrganismFactory.Create(
                    id, genome, Config.Naming, Organism.EnergyCeiling, x, y, brain,
                    Morphology.Capacity(genome, Config.Multicellular));
                _organisms[id] = organism;
                _occupancy[(x, y)] = id;
                _lineageRecords[id] = new LineageEntry(id, parentId: null, lineageId: id, birthTick: 0, generationDepth: 0, birthTraits: genome);
                placed = true;
            }

            if (!placed)
            {
                throw new InvalidOperationException(
                    "Unable to place a genesis organism on a free Grassland tile within the attempt budget.");
            }
        }
    }

    /// <summary>
    /// Organisms occupying the 3×3 block centered on this one (including itself) — the crowding
    /// measure a density plague drains against (lifesim.md §6). An integer count, so it is
    /// order-independent and safe to read from settled post-movement occupancy.
    /// </summary>
    private int LocalOrganismDensity(Organism organism)
    {
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (_occupancy.ContainsKey((organism.X + dx, organism.Y + dy)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private void RefreshExtinction()
    {
        if (_organisms.Count == 0)
        {
            Extinct = true;
        }
    }

    /// <summary>
    /// Builds the tick's analytics (lifesim.md §14) from the settled world state plus the tick's
    /// flow <paramref name="counters"/>. Every cross-organism reduction iterates
    /// <see cref="_organisms"/> in ascending id order (a <see cref="SortedDictionary{TKey,TValue}"/>)
    /// so float sums have a fixed reduction order (lifesim.md §9).
    /// </summary>
    private SimulationMetrics BuildMetrics(TickCounters counters)
    {
        int population = _organisms.Count;
        TraitBounds bounds = Config.TraitBounds;

        double energyMin = 0.0, energyMax = 0.0, energySum = 0.0;
        double sumSize = 0, sumSpeed = 0, sumThermalC = 0, sumThermalW = 0, sumEnv = 0, sumOrg = 0, sumAcuity = 0, sumShare = 0, sumCells = 0;

        var biomeCounts = new Dictionary<Biome, long>
        {
            [Biome.Grassland] = 0,
            [Biome.Desert] = 0,
            [Biome.Swamp] = 0,
            [Biome.IceSheet] = 0,
        };

        var sizeBuckets = new int[HistogramBucketCount];
        var speedBuckets = new int[HistogramBucketCount];
        var thermalCenterBuckets = new int[HistogramBucketCount];
        var thermalWidthBuckets = new int[HistogramBucketCount];
        var envRadiusBuckets = new int[HistogramBucketCount];
        var orgRadiusBuckets = new int[HistogramBucketCount];
        var acuityBuckets = new int[HistogramBucketCount];
        var shareBuckets = new int[HistogramBucketCount];
        var cellBuckets = new int[HistogramBucketCount];

        bool first = true;
        foreach (Organism organism in _organisms.Values)
        {
            double energy = organism.Energy;
            if (first)
            {
                energyMin = energyMax = energy;
                first = false;
            }
            else
            {
                energyMin = Math.Min(energyMin, energy);
                energyMax = Math.Max(energyMax, energy);
            }

            energySum += energy;

            Genome g = organism.Genome;
            sumSize += g.Size;
            sumSpeed += g.SpeedCapacity;
            sumThermalC += g.ThermalCenter;
            sumThermalW += g.ThermalWidth;
            sumEnv += g.EnvRadius;
            sumOrg += g.OrgRadius;
            sumAcuity += g.SensoryAcuity;
            sumShare += g.ShareFraction;
            sumCells += Morphology.CellCount(g, Config.Multicellular);

            biomeCounts[_terrain.BiomeAt(organism.X, organism.Y)]++;

            sizeBuckets[BucketIndex(g.Size, bounds.Size)]++;
            speedBuckets[BucketIndex(g.SpeedCapacity, bounds.SpeedCapacity)]++;
            thermalCenterBuckets[BucketIndex(g.ThermalCenter, bounds.ThermalCenter)]++;
            thermalWidthBuckets[BucketIndex(g.ThermalWidth, bounds.ThermalWidth)]++;
            envRadiusBuckets[BucketIndex(g.EnvRadius, bounds.EnvRadius)]++;
            orgRadiusBuckets[BucketIndex(g.OrgRadius, bounds.OrgRadius)]++;
            acuityBuckets[BucketIndex(g.SensoryAcuity, bounds.SensoryAcuity)]++;
            shareBuckets[BucketIndex(g.ShareFraction, bounds.ShareFraction)]++;
            cellBuckets[BucketIndex(Morphology.CellCount(g, Config.Multicellular), bounds.CellCount)]++;
        }

        double Average(double sum) => population > 0 ? sum / population : 0.0;

        // Reproduction by lineage: births (records that have a parent) grouped by lineage, then
        // restricted to lineages that still have living members, in ascending lineage-id order.
        var birthsByLineage = new Dictionary<long, long>();
        foreach (LineageEntry entry in _lineageRecords.Values)
        {
            if (entry.ParentId is not null)
            {
                birthsByLineage[entry.LineageId] = birthsByLineage.GetValueOrDefault(entry.LineageId) + 1;
            }
        }

        var livingLineageIds = new SortedSet<long>();
        foreach (long id in _organisms.Keys)
        {
            livingLineageIds.Add(_lineageRecords[id].LineageId);
        }

        var reproductionByLineage = livingLineageIds
            .Select(lineageId => new LineageReproduction { LineageId = lineageId, Births = birthsByLineage.GetValueOrDefault(lineageId) })
            .ToList();

        return new SimulationMetrics
        {
            Population = population,
            Extinct = Extinct,
            Births = counters.Births,
            Deaths = counters.Deaths,
            SuccessfulGrazing = counters.SuccessfulGrazing,
            FailedGrazing = counters.FailedGrazing,
            SuccessfulPredation = counters.SuccessfulPredation,
            FailedPredation = counters.FailedPredation,
            SuccessfulShare = counters.SuccessfulShare,
            FailedShare = counters.FailedShare,
            KinPredation = counters.KinPredation,
            EnergyShared = counters.EnergyShared,
            EnergyMin = population > 0 ? energyMin : 0.0,
            EnergyAverage = Average(energySum),
            EnergyMax = population > 0 ? energyMax : 0.0,
            TraitAverages = new TraitAverages
            {
                Size = Average(sumSize),
                SpeedCapacity = Average(sumSpeed),
                ThermalCenter = Average(sumThermalC),
                ThermalWidth = Average(sumThermalW),
                EnvRadius = Average(sumEnv),
                OrgRadius = Average(sumOrg),
                SensoryAcuity = Average(sumAcuity),
                ShareFraction = Average(sumShare),
                CellCount = Average(sumCells),
            },
            TraitHistograms =
            [
                Histogram("size", bounds.Size, sizeBuckets),
                Histogram("speed_capacity", bounds.SpeedCapacity, speedBuckets),
                Histogram("thermal_center", bounds.ThermalCenter, thermalCenterBuckets),
                Histogram("thermal_width", bounds.ThermalWidth, thermalWidthBuckets),
                Histogram("env_radius", bounds.EnvRadius, envRadiusBuckets),
                Histogram("org_radius", bounds.OrgRadius, orgRadiusBuckets),
                Histogram("sensory_acuity", bounds.SensoryAcuity, acuityBuckets),
                Histogram("share_fraction", bounds.ShareFraction, shareBuckets),
                Histogram("cell_count", bounds.CellCount, cellBuckets),
            ],
            PopulationByBiome =
            [
                new BiomePopulation { Biome = Biome.Grassland, Count = biomeCounts[Biome.Grassland] },
                new BiomePopulation { Biome = Biome.Desert, Count = biomeCounts[Biome.Desert] },
                new BiomePopulation { Biome = Biome.Swamp, Count = biomeCounts[Biome.Swamp] },
                new BiomePopulation { Biome = Biome.IceSheet, Count = biomeCounts[Biome.IceSheet] },
            ],
            ReproductionByLineage = reproductionByLineage,
            ActiveEvents = _environment.Modifiers.Select(m => m.Type).ToList(),
        };
    }

    private static TraitHistogram Histogram(string trait, TraitBounds.Range range, int[] buckets) => new()
    {
        Trait = trait,
        Min = range.Min,
        Max = range.Max,
        Buckets = [.. buckets],
    };

    private static int BucketIndex(double value, TraitBounds.Range range)
    {
        if (range.Max <= range.Min)
        {
            return 0;
        }

        double t = (value - range.Min) / (range.Max - range.Min);
        return Math.Clamp((int)(t * HistogramBucketCount), 0, HistogramBucketCount - 1);
    }

    /// <summary>Mutable per-tick flow tallies, folded into <see cref="SimulationMetrics"/> in the Metrics phase (lifesim.md §14).</summary>
    private sealed class TickCounters
    {
        public long Births;
        public long Deaths;
        public long SuccessfulGrazing;
        public long FailedGrazing;
        public long SuccessfulPredation;
        public long FailedPredation;
        public long SuccessfulShare;
        public long FailedShare;
        public long KinPredation;
        public double EnergyShared;
    }

    private sealed record PendingBirth(
        long OffspringId, long ParentId, Genome Genome, NeatGenome Brain, double OffspringEnergy,
        int X, int Y, long BirthTick, long LineageId, int GenerationDepth);
}
