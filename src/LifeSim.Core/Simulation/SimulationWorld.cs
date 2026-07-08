using LifeSim.Core.Brains;
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
///. Offspring inherit their parent's genome and brain with mutation applied in the
/// Birth Commit phase.
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
    private readonly OccupancyGrid _occupancy;

    /// <summary>Number of bins in each per-trait distribution histogram.</summary>
    private const int HistogramBucketCount = 10;

    /// <summary>Metrics for the most recently completed tick; rebuilt every Metrics phase.</summary>
    private SimulationMetrics _metrics = new();

    /// <summary>Provenance of UI interventions, preserved across resume so it never silently drops.</summary>
    private List<EditLogEntry> _editLog = [];

    // Branch provenance; carried verbatim across resume, never minted by the engine.
    private string? _snapshotId;
    private string? _parentSnapshotId;
    private string? _branchId;

    /// <summary>Ancestry records for every organism that has ever lived — never removed, unlike <see cref="_organisms"/>.</summary>
    private readonly SortedDictionary<long, LineageEntry> _lineageRecords = new();

    /// <summary>
    /// Running count of births (offspring records, i.e. lineage entries with a parent) per lineage id,
    /// maintained incrementally at birth commit. Births only ever accumulate, so this avoids rescanning
    /// the entire (unbounded) <see cref="_lineageRecords"/> history every tick in <see cref="BuildMetrics"/>.
    /// </summary>
    private readonly Dictionary<long, long> _birthsByLineage = new();

    public WorldState World { get; }

    public SimulationConfig Config { get; }

    public long Tick { get; private set; }

    /// <summary>
    /// Max threads for the per-organism brain forward pass. Purely an execution knob:
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

    /// <summary>Set once population reaches zero; the engine halts and never auto-reseeds.</summary>
    public bool Extinct { get; private set; }

    /// <summary>Living organisms in ascending id order.</summary>
    public IReadOnlyDictionary<long, Organism> Organisms => _organisms;

    /// <summary>Ancestry records for every organism that has ever lived, ascending by id.</summary>
    public IReadOnlyDictionary<long, LineageEntry> LineageRecords => _lineageRecords;

    /// <summary>The most recently computed tick metrics — the same object <see cref="ToSnapshot"/> carries.</summary>
    public SimulationMetrics Metrics => _metrics;

    private SimulationWorld(
        WorldState world, SimulationConfig config, TerrainSampler terrain, GroundEnergyGrid groundEnergy,
        PrngStreams prngStreams, OrganismIdAllocator idAllocator, InnovationIdAllocator innovationIdAllocator,
        EnvironmentState environment)
    {
        World = world;
        Config = config;
        _occupancy = new OccupancyGrid(world.Width, world.Height);
        _terrain = terrain;
        _groundEnergy = groundEnergy;
        _prngStreams = prngStreams;
        _idAllocator = idAllocator;
        _innovationIdAllocator = innovationIdAllocator;
        _environment = environment;
        _sensoryInputBuilder = new SensoryInputBuilder(terrain, groundEnergy, config, world);
    }

    /// <summary>
    /// Builds Tick 0: scatters <see cref="SimulationConfig.InitialPopulation"/>
    /// mid-range-genome organisms across Grassland tiles via the genesis PRNG stream.
    /// </summary>
    /// <summary>
    /// Builds Tick 0. Terrain is always seeded from <see cref="WorldState.Seed"/> (the map is
    /// deterministic), but the gameplay PRNG streams — behaviour, mutation, combat, events, genesis —
    /// are seeded from <paramref name="simulationSeed"/> when supplied, else from the world seed. Pass
    /// an entropy value for a run whose life differs every time on the same map; pass nothing (or the
    /// world seed) for a fully reproducible run. Either way the stream state is captured in snapshots,
    /// so save/reload/replay of a created world still hold.
    /// </summary>
    public static SimulationWorld CreateGenesis(WorldState world, SimulationConfig config, ulong? simulationSeed = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(config);

        var terrain = new TerrainSampler(world.Seed, config);
        var groundEnergy = new GroundEnergyGrid(terrain, config);
        var prngStreams = PrngStreams.FromSeed(simulationSeed ?? world.Seed);
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
            // Capacity is a deterministic function of genome + config, recomputed on
            // load rather than stored — so save/reload stays byte-identical.
            Organism organism = entry.ToOrganism(Morphology.Capacity(entry.Genome.ToGenome(), snapshot.Configuration.Multicellular));
            simWorld._organisms[organism.Id] = organism;
            simWorld._occupancy.Set(organism.X, organism.Y, organism.Id);
        }

        foreach (LineageSnapshot entry in snapshot.Lineages)
        {
            LineageEntry lineage = entry.ToEntry();
            simWorld._lineageRecords[lineage.OrganismId] = lineage;

            // Rebuild the running births-per-lineage tally from history: an entry with a parent is a birth.
            if (lineage.ParentId is not null)
            {
                simWorld._birthsByLineage[lineage.LineageId] = simWorld._birthsByLineage.GetValueOrDefault(lineage.LineageId) + 1;
            }
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
            // Advanced by structural brain mutations in the Birth Commit phase;
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

    /// <summary>Advances exactly one tick through the authoritative phase order.</summary>
    public void Advance()
    {
        if (Extinct)
        {
            throw new InvalidOperationException("Cannot advance an extinct (halted) world.");
        }

        // The tick this Advance() call is producing (Tick itself only increments at the very end,
        // in phase 9) — used consistently for birth_tick/death_tick/last_birth_tick bookkeeping.
        long currentTick = Tick + 1;

        // 1. Environment Phase: age/expire active event modifiers, then roll for new stochastic
        //    events against the events stream. Newly-triggered events are live
        //    for the rest of this same tick — the Sensing phase below already sees them.
        _environment.RunEnvironmentPhase(_prngStreams[PrngStream.Events], Config.Events, currentTick);

        // 2. Sensing Phase: every organism's input vector is built from world state as it stands
        //    at the start of the tick — positions/energy are still whatever the previous tick's
        //    Intent Resolution left them, since this phase runs in full before any organism's
        //    decision is even made, let alone resolved.
        //    Sensory noise is the only randomness in this phase. Rather than draw every organism's noise
        //    from one shared stream in id order — which a parallel build would consume out of order — we
        //    draw a single per-tick seed from the stream, then derive each organism's own noise PRNG from
        //    that seed and its organism id (SplitMix64). The derivation is order-independent and
        //    reproducible, so the input build runs across up to MaxDegreeOfParallelism threads with
        //    byte-identical results for any thread count (and across save/reload, since the seed comes
        //    from the restored stream).
        // The organism set is fixed from here through the Death & Transfer phase — offspring are only
        // reserved during Intent Resolution and not inserted into the live index until Birth Commit — so
        // every phase in between shares one id list (ascending, the priority order) and keys its
        // per-organism scratch by array position instead of re-snapshotting the key set and re-hashing by
        // id in each phase.
        long[] tickIds = _organisms.Keys.ToArray();
        int organismCount = tickIds.Length;

        double globalStress = _environment.GlobalStress;
        double temperatureOffset = _environment.TemperatureOffset;
        ulong sensoryNoiseSeed = _prngStreams[PrngStream.SensoryNoise].NextULong();
        var sensoryInputs = new double[organismCount][];
        RunPhase(organismCount, i =>
        {
            long id = tickIds[i];
            var noise = new Prng(SplitMix64.Finalize(sensoryNoiseSeed + (ulong)id));
            sensoryInputs[i] = _sensoryInputBuilder.Build(
                _organisms[id], _organisms, currentTick, noise, globalStress, temperatureOffset);
        });

        // 3. Decision Phase. Per-organism NEAT evaluation is independent of every other organism
        //    (each reads only its own cached inputs and its own prior brain state). The expensive
        //    forward pass is pure — no PRNG, no shared writes — so it runs across up to
        //    MaxDegreeOfParallelism threads. The single softmax roll then draws from
        //    the shared behavior stream strictly in ascending organism-id order, so the stream is
        //    consumed identically regardless of the parallelism — results are byte-identical for any
        //    thread count.
        // A larger body runs more recurrent propagation steps per tick, so it
        // reasons more deeply before acting; a single cell runs one step (the base model).
        Prng behavior = _prngStreams[PrngStream.Behavior];
        MulticellularConfig decisionMc = Config.Multicellular;
        var propagations = new NeatPropagation[organismCount];
        RunPhase(organismCount, i => propagations[i] = Decide(tickIds[i], sensoryInputs[i], decisionMc));

        var actions = new OrganismAction[organismCount];
        for (int i = 0; i < organismCount; i++)
        {
            Organism organism = _organisms[tickIds[i]];
            OrganismAction action = NeatBrain.SelectAction(propagations[i].Probabilities, behavior);
            organism.UpdateBrain(propagations[i].Genome);
            organism.RecordAction(action);
            actions[i] = action;
        }

        // 4. Intent Resolution Phase: movement, harvest (grazing/predation), and reproduction
        //    validity/tile-claiming all resolve here, in a single ascending-id pass, since they
        //    share occupancy state and must interleave in strict organism-id priority order
        //   . Offspring are only *reserved* here (tile claimed, parent
        //    charged, id allocated) — actual insertion into the live index happens in the Birth
        //    Commit phase below.
        var counters = new TickCounters();
        var distanceTraveled = new double[organismCount];
        var pendingBirths = new List<PendingBirth>();

        // Sexual reproduction consumes a partner: once an organism has mated this tick (as initiator or
        // co-opted partner) it can't be booked again. Serial intent phase, so a plain set suffices.
        var matedThisTick = new HashSet<long>();

        // Energy at the start of the tick (before intent), used as the reward signal for within-life
        // learning: net energy change over the tick tells a plastic brain whether its behaviour paid off.
        var tickStartEnergy = new double[organismCount];
        for (int i = 0; i < organismCount; i++)
        {
            tickStartEnergy[i] = _organisms[tickIds[i]].Energy;
        }

        for (int i = 0; i < organismCount; i++)
        {
            Organism organism = _organisms[tickIds[i]];
            if (!organism.IsAlive)
            {
                // Killed by an earlier (lower-id) organism's predation this same tick; a corpse
                // doesn't get to act.
                distanceTraveled[i] = 0.0;
                continue;
            }

            (double distance, ActionResult result) = ResolveIntent(organism, actions[i], currentTick, behavior, pendingBirths, matedThisTick, counters);
            distanceTraveled[i] = distance;
            organism.RecordActionResult(result);
        }

        // 5. Metabolism Phase. Base + thermal stress + sensory tax + movement, plus
        //    any active-event drains: a climatic anomaly shifts the effective tile temperature that
        //    thermal stress is measured against, and a density plague drains organisms crowded
        //    above the configured threshold.
        //    Each organism's cost is a pure function of read-only state (terrain, occupancy, its own
        //    genome/age, its own movement) and it writes only its own energy/age — no PRNG, no shared
        //    writes — so the body runs across up to MaxDegreeOfParallelism threads with byte-identical
        //    results for any thread count. Each captures its own pre-metabolism energy first (the Death &
        //    Transfer phase reads it for corpse energy) into a slot only it owns.
        double temperatureShift = _environment.TemperatureOffset;
        bool plagueActive = _environment.PlagueActive;
        var energyBeforeMetabolism = new double[organismCount];

        RunPhase(organismCount, index =>
        {
            Organism organism = _organisms[tickIds[index]];
            energyBeforeMetabolism[index] = organism.Energy;

            double tileTemperature = _terrain.TemperatureCelsiusAt(organism.X, organism.Y) + temperatureShift;
            double friction = Config.Biomes.For(_terrain.BiomeAt(organism.X, organism.Y)).Friction;
            int localDensity = LocalOrganismDensity(organism); // 3×3 including self

            // Body economy: one cell's base metabolism plus the multicellular overhead
            // for the rest — extra-cell upkeep (volume, ∝ N) + coordination, discounted by division of
            // labour so a well-differentiated body is far cheaper than a lopsided one. Defender cells
            // insulate against thermal stress and Mover cells cut locomotion tax. A generalist 1-cell
            // body reduces to the plain per-organism cost.
            Genome g = organism.Genome;
            MulticellularConfig mc = Config.Multicellular;
            double perCellBase = Metabolism.BaseMetabolism(g, Config.Metabolism);

            // Self-generated running costs — the price of the body existing, sensing, and moving — are
            // the ones the evolvable metabolic_efficiency trait scales down (toward, never to, zero).
            double selfCost = perCellBase
                + Morphology.MulticellularOverhead(g, perCellBase, mc)
                + Metabolism.SensoryTax(g, Config.Metabolism)
                + Metabolism.DefenseTax(g, Config.Metabolism)
                + Metabolism.PlasticityTax(g, Config.Metabolism)
                + (Metabolism.LocomotionTax(distanceTraveled[index], g.SpeedCapacity, friction, Config.MovementCombat) * Morphology.LocomotionFactor(g, mc));

            // Thermal stress and crowding are externally imposed (climate, neighbours), not self-generated
            // upkeep, so efficiency doesn't discount them — they have their own adaptation levers.
            double cost = (selfCost * Metabolism.EfficiencyCostMultiplier(g, Config.Metabolism))
                + (Metabolism.ThermalStress(g, tileTemperature, Config.Metabolism) * Morphology.ThermalStressFactor(g, mc))
                + Metabolism.CrowdingTax(localDensity - 1, Config.Metabolism); // density-dependent overpopulation cost

            if (Config.Senescence)
            {
                cost += Metabolism.SenescenceTax(organism.Age, Config.Metabolism); // optional aging model
            }

            if (plagueActive && localDensity >= Config.Events.PlagueDensityThreshold)
            {
                cost += Config.Events.PlagueEnergyDrainPerTick;
            }

            organism.SpendEnergy(cost);
            organism.Tick();

            // Within-life learning: nudge the live brain's weights toward what was active when this
            // tick's net energy reward arrived. Deterministic and per-organism (writes only its own
            // brain), so it stays thread-count-safe. Only plastic, still-living organisms learn.
            if (g.Plasticity > 0.0 && organism.IsAlive)
            {
                double reward = organism.Energy - tickStartEnergy[index];
                organism.UpdateBrain(HebbianLearning.Apply(organism.Brain, organism.Germline, reward, g.Plasticity, g.LearningDecay, Config.Learning));
            }
        });

        // 6. Death & Transfer Phase. Predation transfer already happened in Intent Resolution;
        //    non-predation deaths (starvation/thermal stress) deposit corpse energy here, based on
        //    the energy they had *before* the fatal Metabolism deduction — a predation victim is
        //    already at exactly 0 by this point, so it correctly contributes no corpse energy
        //   .
        for (int i = 0; i < organismCount; i++)
        {
            long id = tickIds[i];
            Organism organism = _organisms[id];
            if (organism.IsAlive)
            {
                continue;
            }

            double corpseEnergy = energyBeforeMetabolism[i] * Config.Events.CorpseEnergyFraction;
            if (corpseEnergy > 0.0)
            {
                _groundEnergy.Deposit(organism.X, organism.Y, corpseEnergy);
            }

            _lineageRecords[id].RecordDeath(currentTick, organism.Genome);
            _organisms.Remove(id);
            _occupancy.Clear(organism.X, organism.Y);
            counters.Deaths++;
        }

        // 7. Resource Regeneration Phase — suspended entirely while a Resource Blight is active
        //   , which is what forces populations off grazing and toward predation.
        if (!_environment.BlightActive)
        {
            _groundEnergy.RegenerateTick();
        }

        // 8. Mutation & Birth Commit Phase. Trait and brain mutation are applied here, in ascending
        //    offspring-id order, so the mutation stream and the innovation-id counter both advance
        //    deterministically. pendingBirths is already ascending by
        //    offspring id (ids are allocated monotonically as births are reserved), but sort
        //    explicitly to make the contract independent of that.
        Prng mutation = _prngStreams[PrngStream.Mutation];
        foreach (PendingBirth birth in pendingBirths.OrderBy(b => b.OffspringId))
        {
            Genome offspringGenome = GenomeMutator.Mutate(birth.Genome, Config.Mutation, Config.TraitBounds, mutation);

            // Multicellular parents bias their offspring toward more cells, so the
            // trait is self-reinforcing; the square-cube economy still caps the viable size. Applied
            // after mutation and re-clamped; draws no randomness, so replay stays deterministic (§9).
            double biasedCells = Morphology.BiasedOffspringCellCount(
                offspringGenome.CellCount, birth.Genome.CellCount, Config.Multicellular);
            offspringGenome = (offspringGenome with { CellCount = biasedCells }).Clamped(Config.TraitBounds);

            NeatGenome offspringBrain = NeatMutator.Mutate(birth.Germline, Config.Mutation, mutation, _innovationIdAllocator);
            Organism offspring = OrganismFactory.Create(
                birth.OffspringId, offspringGenome, Config.Naming, birth.OffspringEnergy, birth.X, birth.Y, offspringBrain,
                Morphology.Capacity(offspringGenome, Config.Multicellular));
            _organisms[offspring.Id] = offspring;
            _lineageRecords[offspring.Id] = new LineageEntry(
                offspring.Id, birth.ParentId, birth.LineageId, birth.BirthTick, birth.GenerationDepth, offspring.Genome,
                foundingType: _lineageRecords[birth.LineageId].FoundingType, // heritable, breeds true down the lineage
                secondParentId: birth.SecondParentId);
            _birthsByLineage[birth.LineageId] = _birthsByLineage.GetValueOrDefault(birth.LineageId) + 1;
        }

        counters.Births = pendingBirths.Count;
        counters.SexualBirths = pendingBirths.Count(b => b.SecondParentId is not null);

        // 9. Metrics & Snapshot Phase.
        Tick++;
        RefreshExtinction();
        _metrics = BuildMetrics(counters);
    }

    private (double Distance, ActionResult Result) ResolveIntent(
        Organism organism, OrganismAction action, long currentTick, Prng behaviorStream,
        List<PendingBirth> pendingBirths, HashSet<long> matedThisTick, TickCounters counters)
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
                return (0.0, ResolveReproduce(organism, currentTick, pendingBirths, matedThisTick));

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
    /// so only the distance actually travelled is paid for.
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

            if (_occupancy.IsOccupied(nextX, nextY))
            {
                break;
            }

            x = nextX;
            y = nextY;
            traveled++;
        }

        if (traveled > 0)
        {
            _occupancy.Clear(organism.X, organism.Y);
            organism.MoveTo(x, y);
            _occupancy.Set(x, y, organism.Id);
        }

        return traveled;
    }

    /// <summary>
    /// The universal Harvest action: grazes ambient ground energy on an
    /// empty target tile, or triggers predatory combat if the target is occupied. Off-grid targets
    /// are a no-op. Outcomes are tallied into <paramref name="counters"/> for metrics:
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
        // index until Birth Commit — there's no one physically there to fight yet,
        // so that falls through to ambient grazing rather than a phantom combat.
        if (_occupancy.TryGet(x, y, out long targetId) && targetId != organism.Id
            && _organisms.TryGetValue(targetId, out Organism? victim))
        {
            // A strike weighs the attacker's combat mass (cells × size, boosted by Defender cells)
            // against the victim's defences — armour, evasion, and toxicity — resolved in ResolveStrike.
            bool killed = ResolveStrike(organism, victim, behaviorStream);

            if (killed)
            {
                double victimEnergy = victim.SpendEnergy(victim.Energy);
                organism.AddEnergy(victimEnergy * Config.MovementCombat.PredationTransferFraction);
                organism.RecordPredationWin();
                counters.SuccessfulPredation++;

                // Kin cannibalism: counted for analytics, and optionally penalised
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

            // The attack failed: the predator pays the failed-combat penalty, and — inherently — the
            // survived victim strikes back. A large, armed, or toxic victim can turn the tables and kill
            // its attacker (whose own armour/evasion/toxicity now apply, defending the counter).
            organism.SpendEnergy(Config.MovementCombat.FailedCombatPenalty);
            organism.RecordPredationLoss();
            counters.FailedPredation++;

            if (ResolveStrike(victim, organism, behaviorStream))
            {
                double attackerEnergy = organism.SpendEnergy(organism.Energy);
                victim.AddEnergy(attackerEnergy * Config.MovementCombat.PredationTransferFraction);
                victim.RecordPredationWin();
                counters.SuccessfulPredation++;
            }

            return ActionResult.Failed;
        }

        // Ambient grazing: a larger body covers more ground, so it skims a footprint
        // of nearby tiles (radius grows with cell count) — pulling energy from more of the surface —
        // while total intake stays bounded by the surface cap (∝ N^⅔). Feeder cells raise the usable
        // yield. A single cell grazes exactly the target tile, as before. Because big bodies drain a
        // wide area (in ascending-id order), they crowd out smaller neighbours and tend to hold
        // territory — an emergent consequence, not a scripted one.
        double gathered = GrazeFootprint(organism, x, y);
        organism.AddEnergy(gathered
            * Morphology.FeedMultiplier(organism.Genome, Config.Multicellular)
            * Metabolism.EfficiencyYieldMultiplier(organism.Genome, Config.Metabolism)); // rate–yield trade-off
        if (gathered > 0.0)
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
    /// Drains a body's grazing footprint centred on (x,y): the target tile first, then
    /// expanding square rings out to <see cref="Morphology.GrazingReach"/>, in a fixed order, until the
    /// surface-bounded intake budget (<see cref="Morphology.MaxGrazingIntake"/>) is spent. Returns the
    /// raw ground energy gathered. Fixed iteration + ascending-id resolution keep it deterministic (§9).
    /// </summary>
    private double GrazeFootprint(Organism organism, int x, int y)
    {
        double budget = Morphology.MaxGrazingIntake(organism.Genome, Config.Multicellular);
        int reach = Morphology.GrazingReach(organism.Genome, Config.Multicellular);
        double gathered = 0.0;

        for (int r = 0; r <= reach && budget > 0.0; r++)
        {
            for (int dy = -r; dy <= r && budget > 0.0; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue; // only the ring exactly r tiles out (inner rings drained already)
                    }

                    int fx = x + dx;
                    int fy = y + dy;
                    if (fx < 0 || fx >= World.Width || fy < 0 || fy >= World.Height)
                    {
                        continue;
                    }

                    double drained = _groundEnergy.Drain(fx, fy, Math.Min(_groundEnergy.EnergyAt(fx, fy), budget));
                    gathered += drained;
                    budget -= drained;
                    if (budget <= 0.0)
                    {
                        break;
                    }
                }
            }
        }

        return gathered;
    }

    /// <summary>
    /// The Share action: donate a fraction of this organism's energy to the live
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
            return ActionResult.NoOp; // cooperation disabled for this world
        }

        int x = organism.X + dx;
        int y = organism.Y + dy;

        if (x < 0 || x >= World.Width || y < 0 || y >= World.Height
            || !_occupancy.TryGet(x, y, out long targetId) || targetId == organism.Id
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

        // Inclusive-fitness accounting: relatedness-weighted energy is the donor's indirect-fitness
        // contribution. Classify the share as kin-directed (helps propagate shared genes — kin selection)
        // vs non-kin (indiscriminate altruism), by the same relatedness threshold used for kin predation.
        // (A strict Hamilton rB > C in raw energy is degenerate here — ShareEfficiency < 1 makes it never
        // hold — so the informative signal is whether sharing is kin-biased.)
        organism.RecordHelpGiven(donated * relatedness);
        if (relatedness >= coop.KinRelatednessThreshold)
        {
            counters.KinDirectedShares++;
        }
        else
        {
            counters.NonKinShares++;
        }

        return ActionResult.Success;
    }

    /// <summary>Asexual reproduction gating and offspring reservation.</summary>
    private ActionResult ResolveReproduce(
        Organism organism, long currentTick, List<PendingBirth> pendingBirths, HashSet<long> matedThisTick)
    {
        Genome g = organism.Genome;

        // A body with too few germ cells is sterile soma — it can support the body but not reproduce.
        if (!Morphology.CanReproduce(g, Config.Multicellular))
        {
            return ActionResult.Failed;
        }

        // Cost scales with body mass, but division of labour sheds the size penalty,
        // so a well-differentiated body reproduces almost as cheaply as a single cell.
        double cost = Config.Reproduction.ReproductionBaseCost * Morphology.ReproductionMass(g, Config.Multicellular);
        if (organism.Energy < cost)
        {
            return ActionResult.Failed;
        }

        if (IsOnReproductionCooldown(organism, currentTick) || matedThisTick.Contains(organism.Id))
        {
            return ActionResult.Failed;
        }

        (int X, int Y)? freeTile = FindFreeAdjacentTile(organism);
        if (freeTile is null)
        {
            return ActionResult.Failed;
        }

        // Sexual vs asexual. Draw the roll only when the trait is expressed, so an all-asexual world
        // (founders start at Sexuality 0) never touches the mating stream and stays byte-identical to the
        // pre-sex baseline. A high-Sexuality organism that finds no willing mate falls back to cloning.
        Organism? mate = null;
        if (g.Sexuality > 0.0 && _prngStreams[PrngStream.Mating].NextDouble() < g.Sexuality)
        {
            mate = FindMate(organism, currentTick, matedThisTick);
        }

        double offspringEnergy = cost * Config.Reproduction.OffspringEnergyFraction;
        LineageEntry parentLineage = _lineageRecords[organism.Id];
        long offspringId = _idAllocator.Allocate();
        _occupancy.Set(freeTile.Value.X, freeTile.Value.Y, offspringId);

        if (mate is not null)
        {
            // Both parents pay (split → total matches an asexual birth; full → each pays the whole cost),
            // both go on cooldown, and both are consumed for the tick. Offspring is a 50/50 blend of the
            // two genomes with recombined germlines (mutation is applied later in the birth-commit phase).
            bool split = Config.Reproduction.SplitReproductionCostAcrossParents;
            double mateCost = Config.Reproduction.ReproductionBaseCost * Morphology.ReproductionMass(mate.Genome, Config.Multicellular);

            organism.SpendEnergy(split ? cost / 2.0 : cost);
            organism.RecordBirth(currentTick);
            mate.SpendEnergy(split ? mateCost / 2.0 : mateCost);
            mate.RecordBirth(currentTick);
            matedThisTick.Add(organism.Id);
            matedThisTick.Add(mate.Id);

            Genome childGenome = Genome.Blend(g, mate.Genome);
            NeatGenome childGermline = NeatCrossover.Recombine(organism.Germline, mate.Germline);

            pendingBirths.Add(new PendingBirth(
                offspringId, organism.Id, childGenome, childGermline, offspringEnergy,
                freeTile.Value.X, freeTile.Value.Y, currentTick, parentLineage.LineageId, parentLineage.GenerationDepth + 1,
                SecondParentId: mate.Id));

            return ActionResult.Success;
        }

        // Asexual clone (either not attempted, or no willing mate in range) — exactly as before.
        organism.SpendEnergy(cost);
        organism.RecordBirth(currentTick);
        pendingBirths.Add(new PendingBirth(
            offspringId, organism.Id, organism.Genome, organism.Germline, offspringEnergy,
            freeTile.Value.X, freeTile.Value.Y, currentTick, parentLineage.LineageId, parentLineage.GenerationDepth + 1));

        return ActionResult.Success;
    }

    private bool IsOnReproductionCooldown(Organism organism, long currentTick) =>
        organism.LastBirthTick is not null
        && currentTick - organism.LastBirthTick.Value < Config.Reproduction.ReproductionCooldownTicks;

    /// <summary>First free in-bounds tile around an organism in the fixed N, S, E, W priority.</summary>
    private (int X, int Y)? FindFreeAdjacentTile(Organism organism)
    {
        foreach ((int ddx, int ddy) in new (int, int)[] { (0, -1), (0, 1), (1, 0), (-1, 0) })
        {
            int x = organism.X + ddx;
            int y = organism.Y + ddy;
            if (x < 0 || x >= World.Width || y < 0 || y >= World.Height || _occupancy.IsOccupied(x, y))
            {
                continue;
            }

            return (x, y);
        }

        return null;
    }

    /// <summary>
    /// Finds a willing mate for sexual reproduction: a sexually-capable (Sexuality &gt; 0), fertile,
    /// off-cooldown, not-already-mated neighbour within <see cref="ReproductionConfig.MateSearchRadius"/>
    /// (Chebyshev) that can afford its share of the cost. Among all candidates the lowest organism id
    /// wins, so the choice is deterministic regardless of scan order. Returns null if none qualifies.
    /// </summary>
    private Organism? FindMate(Organism initiator, long currentTick, HashSet<long> matedThisTick)
    {
        int radius = Config.Reproduction.MateSearchRadius;
        bool split = Config.Reproduction.SplitReproductionCostAcrossParents;
        Organism? best = null;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                if (!_occupancy.TryGet(initiator.X + dx, initiator.Y + dy, out long id)
                    || id == initiator.Id
                    || matedThisTick.Contains(id))
                {
                    continue;
                }

                // A tile can hold an offspring reserved earlier this tick that isn't in the live index
                // until the birth-commit phase — it isn't a valid mate, so skip ids we can't resolve.
                if (!_organisms.TryGetValue(id, out Organism? candidate))
                {
                    continue;
                }

                if (!candidate.IsAlive
                    || candidate.Genome.Sexuality <= 0.0
                    || !Morphology.CanReproduce(candidate.Genome, Config.Multicellular)
                    || IsOnReproductionCooldown(candidate, currentTick))
                {
                    continue;
                }

                double mateCost = Config.Reproduction.ReproductionBaseCost * Morphology.ReproductionMass(candidate.Genome, Config.Multicellular);
                if (candidate.Energy < (split ? mateCost / 2.0 : mateCost))
                {
                    continue;
                }

                if (best is null || candidate.Id < best.Id)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The pure per-organism forward pass for the Decision phase: runs the brain for
    /// <see cref="Morphology.BrainSteps"/> recurrent steps (more for a larger body). Reads only its own
    /// organism and cached inputs and writes no shared state, so it is safe to run concurrently.
    /// </summary>
    private NeatPropagation Decide(long id, double[] inputs, MulticellularConfig mc)
    {
        Organism organism = _organisms[id];
        return NeatBrain.Propagate(organism.Brain, inputs, Morphology.BrainSteps(organism.Genome, mc));
    }

    /// <summary>
    /// Runs a per-index phase <paramref name="body"/> over <paramref name="count"/> indices across up to
    /// <see cref="MaxDegreeOfParallelism"/> threads when it pays off, else serially. The body MUST be
    /// order-independent: it may read shared read-only state and may write only state private to its own
    /// index (its own organism, or an array slot it alone owns), and must not touch a shared PRNG stream.
    /// Under those rules the result is byte-identical for any thread count — the invariant the
    /// determinism suite (incl. the thread-count equivalence test) pins. Sensing, Decision, and
    /// Metabolism all satisfy this.
    /// </summary>
    private void RunPhase(int count, Action<int> body)
    {
        if (_maxDegreeOfParallelism > 1 && count > 1)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = _maxDegreeOfParallelism };
            Parallel.For(0, count, options, body);
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                body(i);
            }
        }
    }

    private void ScatterGenesisPopulation()
    {
        Prng genesis = _prngStreams[PrngStream.Genesis];
        int maxAttempts = Math.Max(10_000, World.Width * World.Height * 4);

        foreach ((NeatGenome? scriptedBrain, string foundingType) in BuildFounderPlan())
        {
            bool placed = false;
            for (int attempt = 0; attempt < maxAttempts && !placed; attempt++)
            {
                int x = genesis.NextInt(World.Width);
                int y = genesis.NextInt(World.Height);

                if (_occupancy.IsOccupied(x, y) || _terrain.BiomeAt(x, y) != Biome.Grassland)
                {
                    continue;
                }

                // Each founder gets its own randomised genome — a varied founding gene pool, not a
                // clone army — so the population is diverse from the start. Thermal preferences are
                // kept comfortable at the grassland they spawn on (a wide band centred near grassland
                // temperature, with spread) so the founding population isn't wiped out before it can
                // evolve; every other trait is fully random, and thermal range expands later by mutation.
                TraitBounds bounds = Config.TraitBounds;
                double thermalWidth = Math.Max(bounds.ThermalWidth.Min, 16.0);
                thermalWidth += genesis.NextDouble() * (bounds.ThermalWidth.Max - thermalWidth);
                double slack = Math.Max(0.0, (thermalWidth / 2.0) - 3.0);
                double thermalCenter = Config.Biomes.Grassland.Temperature + (((genesis.NextDouble() * 2.0) - 1.0) * slack);
                Genome genome = Genome.Random(bounds, genesis) with { ThermalCenter = thermalCenter, ThermalWidth = thermalWidth };
                long id = _idAllocator.Allocate();

                // A scripted type seeds an author-chosen brain (all founders of that type share the same
                // seed); a generic founder draws a fresh random brain. Either way the body is randomised
                // and the brain evolves from here. Generic keeps the exact prior genesis-stream draws.
                NeatGenome brain = scriptedBrain ?? NeatGenomeFactory.CreateMinimalFullyConnected(genesis);
                Organism organism = OrganismFactory.Create(
                    id, genome, Config.Naming, Organism.EnergyCeiling, x, y, brain,
                    Morphology.Capacity(genome, Config.Multicellular));
                _organisms[id] = organism;
                _occupancy.Set(x, y, id);
                _lineageRecords[id] = new LineageEntry(id, parentId: null, lineageId: id, birthTick: 0, generationDepth: 0, birthTraits: genome, foundingType: foundingType);
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
    /// The ordered list of founders to place: one entry per organism, carrying its seed brain (null =
    /// generic random brain) and its founding-type label. Driven by
    /// <see cref="SimulationConfig.FoundingComposition"/> when it specifies any positive count, else a
    /// flat run of <see cref="SimulationConfig.InitialPopulation"/> generic founders (the historic
    /// behaviour, byte-identical). Scripted types are compiled once each; a bad script throws here, at
    /// world creation, with the parser's message.
    /// </summary>
    private IEnumerable<(NeatGenome? Brain, string FoundingType)> BuildFounderPlan()
    {
        IReadOnlyList<BrainTypeSpec> composition = Config.FoundingComposition;
        if (composition.Count == 0 || composition.All(s => s.Count <= 0))
        {
            for (int i = 0; i < Config.InitialPopulation; i++)
            {
                yield return (null, "Generic");
            }

            yield break;
        }

        foreach (BrainTypeSpec spec in composition)
        {
            if (spec.Count <= 0)
            {
                continue;
            }

            NeatGenome? brain = string.IsNullOrWhiteSpace(spec.Script)
                ? null
                : BrainTemplateCompiler.Compile(BrainScriptParser.ParseTemplate(spec.Script));

            for (int i = 0; i < spec.Count; i++)
            {
                yield return (brain, spec.Name);
            }
        }
    }

    /// <summary>
    /// Organisms occupying the 3×3 block centered on this one (including itself) — the crowding
    /// measure a density plague drains against. An integer count, so it is
    /// order-independent and safe to read from settled post-movement occupancy.
    /// </summary>
    /// <summary>
    /// Resolves one predatory strike. First the defender's toxicity is charged to the attacker as
    /// contact damage (win or lose), then a kill is rolled from the attacker's combat mass against the
    /// defender's mass modified by its armour (toughness) and evasion (dodge). Draws exactly one value
    /// from the behaviour stream, so per-tick stream consumption stays deterministic. Shared by the
    /// initial attack and the victim's inherent counterattack.
    /// </summary>
    private bool ResolveStrike(Organism attacker, Organism defender, Prng behaviorStream)
    {
        MovementCombatConfig combat = Config.MovementCombat;
        double toxin = Combat.ToxinContactDamage(defender.Genome, combat);
        if (toxin > 0.0)
        {
            attacker.SpendEnergy(toxin);
        }

        double killProbability = Combat.KillProbability(
            Morphology.CombatMass(attacker.Genome, Config.Multicellular),
            Morphology.CombatMass(defender.Genome, Config.Multicellular),
            defender.Genome,
            combat);
        return behaviorStream.NextDouble() < killProbability;
    }

    private int LocalOrganismDensity(Organism organism)
    {
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (_occupancy.IsOccupied(organism.X + dx, organism.Y + dy))
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
    /// Builds the tick's analytics from the settled world state plus the tick's
    /// flow <paramref name="counters"/>. Every cross-organism reduction iterates
    /// <see cref="_organisms"/> in ascending id order (a <see cref="SortedDictionary{TKey,TValue}"/>)
    /// so float sums have a fixed reduction order.
    /// </summary>
    private SimulationMetrics BuildMetrics(TickCounters counters)
    {
        int population = _organisms.Count;
        TraitBounds bounds = Config.TraitBounds;

        double energyMin = 0.0, energyMax = 0.0, energySum = 0.0;
        double sumSize = 0, sumSpeed = 0, sumThermalC = 0, sumThermalW = 0, sumEnv = 0, sumOrg = 0, sumAcuity = 0, sumEfficiency = 0, sumShare = 0, sumCells = 0;
        double sumArmour = 0, sumEvasion = 0, sumToxicity = 0, sumHelp = 0, sumPlasticity = 0, sumLearningDecay = 0, sumSexuality = 0;

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
        var efficiencyBuckets = new int[HistogramBucketCount];
        var armourBuckets = new int[HistogramBucketCount];
        var evasionBuckets = new int[HistogramBucketCount];
        var toxicityBuckets = new int[HistogramBucketCount];
        var plasticityBuckets = new int[HistogramBucketCount];
        var learningDecayBuckets = new int[HistogramBucketCount];
        var sexualityBuckets = new int[HistogramBucketCount];
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
            sumHelp += organism.HelpGiven;

            Genome g = organism.Genome;
            sumSize += g.Size;
            sumSpeed += g.SpeedCapacity;
            sumThermalC += g.ThermalCenter;
            sumThermalW += g.ThermalWidth;
            sumEnv += g.EnvRadius;
            sumOrg += g.OrgRadius;
            sumAcuity += g.SensoryAcuity;
            sumEfficiency += g.MetabolicEfficiency;
            sumArmour += g.Armour;
            sumEvasion += g.Evasion;
            sumToxicity += g.Toxicity;
            sumPlasticity += g.Plasticity;
            sumLearningDecay += g.LearningDecay;
            sumSexuality += g.Sexuality;
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
            efficiencyBuckets[BucketIndex(g.MetabolicEfficiency, bounds.MetabolicEfficiency)]++;
            armourBuckets[BucketIndex(g.Armour, bounds.Armour)]++;
            evasionBuckets[BucketIndex(g.Evasion, bounds.Evasion)]++;
            toxicityBuckets[BucketIndex(g.Toxicity, bounds.Toxicity)]++;
            plasticityBuckets[BucketIndex(g.Plasticity, bounds.Plasticity)]++;
            learningDecayBuckets[BucketIndex(g.LearningDecay, bounds.LearningDecay)]++;
            sexualityBuckets[BucketIndex(g.Sexuality, bounds.Sexuality)]++;
            shareBuckets[BucketIndex(g.ShareFraction, bounds.ShareFraction)]++;
            cellBuckets[BucketIndex(Morphology.CellCount(g, Config.Multicellular), bounds.CellCount)]++;
        }

        double Average(double sum) => population > 0 ? sum / population : 0.0;

        // Reproduction by lineage: the running births-per-lineage tally (maintained incrementally at
        // birth commit, so no rescan of the unbounded lineage history here) restricted to lineages that
        // still have living members, in ascending lineage-id order.
        var livingLineageIds = new SortedSet<long>();
        foreach (long id in _organisms.Keys)
        {
            livingLineageIds.Add(_lineageRecords[id].LineageId);
        }

        var reproductionByLineage = livingLineageIds
            .Select(lineageId => new LineageReproduction { LineageId = lineageId, Births = _birthsByLineage.GetValueOrDefault(lineageId) })
            .ToList();

        // Living headcount per seeded brain type — the scoreboard for "which type is winning". Ordinal
        // sort keeps the output order deterministic.
        var populationByFoundingType = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (long id in _organisms.Keys)
        {
            string foundingType = _lineageRecords[id].FoundingType;
            populationByFoundingType[foundingType] = populationByFoundingType.GetValueOrDefault(foundingType) + 1;
        }

        return new SimulationMetrics
        {
            Population = population,
            Extinct = Extinct,
            Births = counters.Births,
            SexualBirths = counters.SexualBirths,
            Deaths = counters.Deaths,
            SuccessfulGrazing = counters.SuccessfulGrazing,
            FailedGrazing = counters.FailedGrazing,
            SuccessfulPredation = counters.SuccessfulPredation,
            FailedPredation = counters.FailedPredation,
            SuccessfulShare = counters.SuccessfulShare,
            FailedShare = counters.FailedShare,
            KinPredation = counters.KinPredation,
            EnergyShared = counters.EnergyShared,
            KinDirectedShares = counters.KinDirectedShares,
            NonKinShares = counters.NonKinShares,
            MeanHelpGiven = Average(sumHelp),
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
                MetabolicEfficiency = Average(sumEfficiency),
                Armour = Average(sumArmour),
                Evasion = Average(sumEvasion),
                Toxicity = Average(sumToxicity),
                Plasticity = Average(sumPlasticity),
                LearningDecay = Average(sumLearningDecay),
                Sexuality = Average(sumSexuality),
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
                Histogram("metabolic_efficiency", bounds.MetabolicEfficiency, efficiencyBuckets),
                Histogram("armour", bounds.Armour, armourBuckets),
                Histogram("evasion", bounds.Evasion, evasionBuckets),
                Histogram("toxicity", bounds.Toxicity, toxicityBuckets),
                Histogram("plasticity", bounds.Plasticity, plasticityBuckets),
                Histogram("learning_decay", bounds.LearningDecay, learningDecayBuckets),
                Histogram("sexuality", bounds.Sexuality, sexualityBuckets),
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
            PopulationByFoundingType = populationByFoundingType
                .Select(kv => new FoundingTypePopulation { Name = kv.Key, Count = kv.Value })
                .ToList(),
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

    /// <summary>Mutable per-tick flow tallies, folded into <see cref="SimulationMetrics"/> in the Metrics phase.</summary>
    private sealed class TickCounters
    {
        public long Births;
        public long SexualBirths;   // of Births, those with two parents (biparental recombination)
        public long Deaths;
        public long SuccessfulGrazing;
        public long FailedGrazing;
        public long SuccessfulPredation;
        public long FailedPredation;
        public long SuccessfulShare;
        public long FailedShare;
        public long KinPredation;
        public double EnergyShared;
        public long KinDirectedShares;   // successful shares to a relative (r >= kin threshold)
        public long NonKinShares;        // successful shares to a non-relative (indiscriminate altruism)
    }

    private sealed record PendingBirth(
        long OffspringId, long ParentId, Genome Genome, NeatGenome Germline, double OffspringEnergy,
        int X, int Y, long BirthTick, long LineageId, int GenerationDepth, long? SecondParentId = null);
}
