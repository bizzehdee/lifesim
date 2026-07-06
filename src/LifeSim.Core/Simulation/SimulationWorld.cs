using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.Sensing;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Simulation;

/// <summary>
/// The tick-loop aggregate root: terrain, ground energy, PRNG streams, and the live organism
/// index, advanced one phased tick at a time in the authoritative order (lifesim.md §7). Mutation
/// and environmental events are stubbed no-ops until the phases that implement them (8-9) land —
/// offspring are exact copies of their parent's genome and brain for now.
/// </summary>
public sealed class SimulationWorld
{
    private readonly TerrainSampler _terrain;
    private readonly GroundEnergyGrid _groundEnergy;
    private readonly PrngStreams _prngStreams;
    private readonly OrganismIdAllocator _idAllocator;
    private readonly SensoryInputBuilder _sensoryInputBuilder;
    private readonly SortedDictionary<long, Organism> _organisms = new();
    private readonly Dictionary<(int X, int Y), long> _occupancy = new();

    /// <summary>Ancestry records for every organism that has ever lived — never removed, unlike <see cref="_organisms"/> (lifesim.md §8, §14).</summary>
    private readonly SortedDictionary<long, LineageEntry> _lineageRecords = new();

    public WorldState World { get; }

    public SimulationConfig Config { get; }

    public long Tick { get; private set; }

    /// <summary>Set once population reaches zero; the engine halts and never auto-reseeds (lifesim.md §17).</summary>
    public bool Extinct { get; private set; }

    /// <summary>Living organisms in ascending id order (lifesim.md §7, §9).</summary>
    public IReadOnlyDictionary<long, Organism> Organisms => _organisms;

    /// <summary>Ancestry records for every organism that has ever lived, ascending by id.</summary>
    public IReadOnlyDictionary<long, LineageEntry> LineageRecords => _lineageRecords;

    private SimulationWorld(
        WorldState world, SimulationConfig config, TerrainSampler terrain,
        GroundEnergyGrid groundEnergy, PrngStreams prngStreams, OrganismIdAllocator idAllocator)
    {
        World = world;
        Config = config;
        _terrain = terrain;
        _groundEnergy = groundEnergy;
        _prngStreams = prngStreams;
        _idAllocator = idAllocator;
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

        var simWorld = new SimulationWorld(world, config, terrain, groundEnergy, prngStreams, idAllocator);
        simWorld.ScatterGenesisPopulation();
        simWorld.RefreshExtinction();
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

        var simWorld = new SimulationWorld(snapshot.World, snapshot.Configuration, terrain, groundEnergy, prngStreams, idAllocator)
        {
            Tick = snapshot.Tick,
            Extinct = snapshot.Metrics?.Extinct ?? false,
        };

        foreach (OrganismSnapshot entry in snapshot.Organisms)
        {
            Organism organism = entry.ToOrganism();
            simWorld._organisms[organism.Id] = organism;
            simWorld._occupancy[(organism.X, organism.Y)] = organism.Id;
        }

        foreach (LineageSnapshot entry in snapshot.Lineages)
        {
            LineageEntry lineage = entry.ToEntry();
            simWorld._lineageRecords[lineage.OrganismId] = lineage;
        }

        return simWorld;
    }

    public WorldSnapshot ToSnapshot() => new()
    {
        Tick = Tick,
        World = World,
        Configuration = Config,
        PrngStreams = _prngStreams.CaptureState(),
        EvolutionBookkeeping = new EvolutionBookkeeping
        {
            // Structural mutations (Phase 8) start allocating after the genesis topology's
            // reserved range; nothing advances this counter yet since genesis wiring uses fixed,
            // deterministic ids rather than drawing from an allocator.
            NextInnovationId = NeatTopology.ReservedInnovationIdCount,
            NextOrganismId = _idAllocator.NextId,
        },
        GroundEnergy = _groundEnergy.CaptureState(),
        Organisms = _organisms.Values.Select(OrganismSnapshot.From).ToList(),
        Lineages = _lineageRecords.Values.Select(LineageSnapshot.From).ToList(),
        Metrics = new SimulationMetrics { Population = _organisms.Count, Extinct = Extinct },
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

        // 1. Environment Phase — stochastic events arrive in Phase 9; nothing to age/expire yet.

        // 2. Sensing Phase: every organism's input vector is built from world state as it stands
        //    at the start of the tick — positions/energy are still whatever the previous tick's
        //    Intent Resolution left them, since this phase runs in full before any organism's
        //    decision is even made, let alone resolved (lifesim.md §7).
        Prng sensoryNoise = _prngStreams[PrngStream.SensoryNoise];
        var sensoryInputs = new Dictionary<long, double[]>(_organisms.Count);
        foreach (long id in _organisms.Keys)
        {
            sensoryInputs[id] = _sensoryInputBuilder.Build(_organisms[id], _organisms, currentTick, sensoryNoise);
        }

        // 3. Decision Phase. Per-organism NEAT evaluation is independent of every other organism
        //    (each reads only its own cached inputs and its own prior brain state), so this loop
        //    is safe to parallelize once that becomes worth doing (lifesim.md §7) — it draws from
        //    one shared behavior stream, so the *order* of the softmax rolls must stay sequential
        //    in ascending organism-id order even if the evaluation work itself is parallelized.
        Prng behavior = _prngStreams[PrngStream.Behavior];
        var actions = new Dictionary<long, OrganismAction>(_organisms.Count);
        foreach (long id in _organisms.Keys)
        {
            Organism organism = _organisms[id];
            NeatEvaluationResult result = NeatBrain.Evaluate(organism.Brain, sensoryInputs[id], behavior);
            organism.UpdateBrain(result.Genome);
            organism.RecordAction(result.Action);
            actions[id] = result.Action;
        }

        // 4. Intent Resolution Phase: movement, harvest (grazing/predation), and reproduction
        //    validity/tile-claiming all resolve here, in a single ascending-id pass, since they
        //    share occupancy state and must interleave in strict organism-id priority order
        //    (lifesim.md §7, §10). Offspring are only *reserved* here (tile claimed, parent
        //    charged, id allocated) — actual insertion into the live index happens in the Birth
        //    Commit phase below.
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

            (double distance, ActionResult result) = ResolveIntent(organism, actions[id], currentTick, behavior, pendingBirths);
            distanceTraveled[id] = distance;
            organism.RecordActionResult(result);
        }

        // 5. Metabolism Phase.
        var energyBeforeMetabolism = new Dictionary<long, double>(_organisms.Count);
        foreach (long id in _organisms.Keys)
        {
            Organism organism = _organisms[id];
            energyBeforeMetabolism[id] = organism.Energy;

            double tileTemperature = _terrain.TemperatureAt(organism.X, organism.Y);
            double friction = Config.Biomes.For(_terrain.BiomeAt(organism.X, organism.Y)).Friction;
            double cost = Metabolism.Total(organism.Genome, tileTemperature, Config.Metabolism)
                + Metabolism.LocomotionTax(distanceTraveled[id], organism.Genome.SpeedCapacity, friction, Config.MovementCombat);
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
        }

        // 7. Resource Regeneration Phase.
        _groundEnergy.RegenerateTick();

        // 8. Mutation & Birth Commit Phase. Mutation arrives in Phase 8; offspring are exact
        //    copies of their parent's genome and brain (with node state reset) for now.
        foreach (PendingBirth birth in pendingBirths)
        {
            Organism offspring = OrganismFactory.Create(
                birth.OffspringId, birth.Genome, Config.Naming, birth.OffspringEnergy, birth.X, birth.Y, birth.Brain);
            _organisms[offspring.Id] = offspring;
            _lineageRecords[offspring.Id] = new LineageEntry(
                offspring.Id, birth.ParentId, birth.LineageId, birth.BirthTick, birth.GenerationDepth, offspring.Genome);
        }

        // 9. Metrics & Snapshot Phase.
        Tick++;
        RefreshExtinction();
    }

    private (double Distance, ActionResult Result) ResolveIntent(
        Organism organism, OrganismAction action, long currentTick, Prng behaviorStream, List<PendingBirth> pendingBirths)
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
                return (0.0, ResolveHarvest(organism, 0, 0, behaviorStream));
            case OrganismAction.HarvestNorth:
                return (0.0, ResolveHarvest(organism, 0, -1, behaviorStream));
            case OrganismAction.HarvestSouth:
                return (0.0, ResolveHarvest(organism, 0, 1, behaviorStream));
            case OrganismAction.HarvestEast:
                return (0.0, ResolveHarvest(organism, 1, 0, behaviorStream));
            case OrganismAction.HarvestWest:
                return (0.0, ResolveHarvest(organism, -1, 0, behaviorStream));

            case OrganismAction.Reproduce:
                return (0.0, ResolveReproduce(organism, currentTick, pendingBirths));

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
    /// are a no-op.
    /// </summary>
    private ActionResult ResolveHarvest(Organism organism, int dx, int dy, Prng behaviorStream)
    {
        int x = organism.X + dx;
        int y = organism.Y + dy;

        if (x < 0 || x >= World.Width || y < 0 || y >= World.Height)
        {
            return ActionResult.NoOp;
        }

        if (_occupancy.TryGetValue((x, y), out long targetId) && targetId != organism.Id)
        {
            Organism victim = _organisms[targetId];
            double killProbability = Combat.KillProbability(organism.Genome.Size, victim.Genome.Size);
            bool killed = behaviorStream.NextDouble() < killProbability;

            if (killed)
            {
                double victimEnergy = victim.SpendEnergy(victim.Energy);
                organism.AddEnergy(victimEnergy * Config.MovementCombat.PredationTransferFraction);
                return ActionResult.Success;
            }

            organism.SpendEnergy(Config.MovementCombat.FailedCombatPenalty);
            return ActionResult.Failed;
        }

        // Ambient grazing: drain whatever's currently on the tile (possibly nothing).
        double drained = _groundEnergy.Drain(x, y, _groundEnergy.EnergyAt(x, y));
        organism.AddEnergy(drained);
        return ActionResult.Success;
    }

    /// <summary>Asexual reproduction gating and offspring reservation (lifesim.md §8, §17).</summary>
    private ActionResult ResolveReproduce(Organism organism, long currentTick, List<PendingBirth> pendingBirths)
    {
        double cost = Config.Reproduction.ReproductionBaseCost * organism.Genome.Size;
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
        Genome genome = Genome.MidRange(Config.TraitBounds);

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
                Organism organism = OrganismFactory.Create(id, genome, Config.Naming, Organism.EnergyCeiling, x, y, brain);
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

    private void RefreshExtinction()
    {
        if (_organisms.Count == 0)
        {
            Extinct = true;
        }
    }

    private sealed record PendingBirth(
        long OffspringId, long ParentId, Genome Genome, NeatGenome Brain, double OffspringEnergy,
        int X, int Y, long BirthTick, long LineageId, int GenerationDepth);
}
