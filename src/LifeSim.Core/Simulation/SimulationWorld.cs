using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Simulation;

/// <summary>
/// The tick-loop aggregate root: terrain, ground energy, PRNG streams, and the live organism
/// index, advanced one phased tick at a time in the authoritative order (lifesim.md §7). Harvest,
/// Reproduce, mutation, and events are stubbed no-ops until the phases that implement them
/// (7-9) land; the fixed sensory vector is a Phase 5 placeholder until Phase 6.
/// </summary>
public sealed class SimulationWorld
{
    private readonly TerrainSampler _terrain;
    private readonly GroundEnergyGrid _groundEnergy;
    private readonly PrngStreams _prngStreams;
    private readonly OrganismIdAllocator _idAllocator;
    private readonly SortedDictionary<long, Organism> _organisms = new();
    private readonly Dictionary<(int X, int Y), long> _occupancy = new();

    public WorldState World { get; }

    public SimulationConfig Config { get; }

    public long Tick { get; private set; }

    /// <summary>Set once population reaches zero; the engine halts and never auto-reseeds (lifesim.md §17).</summary>
    public bool Extinct { get; private set; }

    /// <summary>Living organisms in ascending id order (lifesim.md §7, §9).</summary>
    public IReadOnlyDictionary<long, Organism> Organisms => _organisms;

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

    /// <summary>Rehydrates a world from a snapshot, restoring PRNG streams, ground energy, and every organism.</summary>
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
        Metrics = new SimulationMetrics { Population = _organisms.Count, Extinct = Extinct },
    };

    /// <summary>Advances exactly one tick through the authoritative phase order (lifesim.md §7).</summary>
    public void Advance()
    {
        if (Extinct)
        {
            throw new InvalidOperationException("Cannot advance an extinct (halted) world (lifesim.md §17).");
        }

        // 1. Environment Phase — stochastic events arrive in Phase 9; nothing to age/expire yet.
        // 2. Sensing Phase — the real fixed input vector arrives in Phase 6; BuildPlaceholderInputs
        //    below is a stand-in fed straight into the Decision Phase.

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
            double[] inputs = BuildPlaceholderInputs(organism);
            NeatEvaluationResult result = NeatBrain.Evaluate(organism.Brain, inputs, behavior);
            organism.UpdateBrain(result.Genome);
            organism.RecordAction(result.Action);
            actions[id] = result.Action;
        }

        // 4. Intent Resolution Phase — only movement is implemented; Harvest/Reproduce are
        //    stubbed no-ops until Phase 7/8.
        var distanceTraveled = new Dictionary<long, double>(_organisms.Count);
        foreach (long id in _organisms.Keys)
        {
            distanceTraveled[id] = ResolveIntent(_organisms[id], actions[id]);
        }

        // 5. Metabolism Phase.
        foreach (long id in _organisms.Keys)
        {
            Organism organism = _organisms[id];
            double tileTemperature = _terrain.TemperatureAt(organism.X, organism.Y);
            double friction = Config.Biomes.For(_terrain.BiomeAt(organism.X, organism.Y)).Friction;
            double cost = Metabolism.Total(organism.Genome, tileTemperature, Config.Metabolism)
                + Metabolism.LocomotionTax(distanceTraveled[id], organism.Genome.SpeedCapacity, friction, Config.MovementCombat);
            organism.SpendEnergy(cost);
            organism.Tick();
        }

        // 6. Death & Transfer Phase — corpse energy and predation transfer arrive in Phase 7.
        foreach (long id in _organisms.Keys.ToArray())
        {
            Organism organism = _organisms[id];
            if (!organism.IsAlive)
            {
                _organisms.Remove(id);
                _occupancy.Remove((organism.X, organism.Y));
            }
        }

        // 7. Resource Regeneration Phase.
        _groundEnergy.RegenerateTick();

        // 8. Mutation & Birth Commit Phase — reproduction and mutation arrive in Phase 7/8.

        // 9. Metrics & Snapshot Phase.
        Tick++;
        RefreshExtinction();
    }

    /// <summary>
    /// Placeholder sensory vector (Phase 5) — energy, age, tile temperature, biome friction, each
    /// roughly scaled into a small numeric range. Replaced wholesale by the full normalized §13
    /// fixed input vector (with acuity-scaled Gaussian noise) in Phase 6.
    /// </summary>
    private double[] BuildPlaceholderInputs(Organism organism)
    {
        double energy = organism.Energy / Organism.EnergyCeiling;
        double age = Math.Tanh(organism.Age / 100.0);
        double tileTemperature = _terrain.TemperatureAt(organism.X, organism.Y) / 50.0;
        double friction = Config.Biomes.For(_terrain.BiomeAt(organism.X, organism.Y)).Friction / 5.0;

        return [energy, age, tileTemperature, friction];
    }

    private double ResolveIntent(Organism organism, OrganismAction action)
    {
        (int Dx, int Dy)? direction = action switch
        {
            OrganismAction.MoveNorth => (0, -1),
            OrganismAction.MoveSouth => (0, 1),
            OrganismAction.MoveEast => (1, 0),
            OrganismAction.MoveWest => (-1, 0),
            _ => null, // Harvest-*, Idle, Reproduce: stubbed no-ops until Phase 7/8.
        };

        if (direction is null)
        {
            return 0.0;
        }

        // Multi-tile movement: step tile-by-tile, stopping at the first off-grid or occupied tile,
        // so only the distance actually travelled is paid for (lifesim.md §10, §17).
        int maxSteps = (int)Math.Floor(organism.Genome.SpeedCapacity);
        int x = organism.X;
        int y = organism.Y;
        int traveled = 0;

        for (int step = 0; step < maxSteps; step++)
        {
            int nextX = x + direction.Value.Dx;
            int nextY = y + direction.Value.Dy;

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
}
