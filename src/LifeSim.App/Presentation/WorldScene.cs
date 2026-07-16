using Avalonia.Media;
using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.App.Presentation;

/// <summary>The selected organism's sensory footprint, highlighted on the map.</summary>
public sealed record OrganismFootprint(double CentreX, double CentreY, double EnvRadius, double OrgRadius);

/// <summary>
/// A fully render-ready view of one frame, built purely from a <see cref="WorldSnapshot"/>. Terrain/biomes are reconstructed from the world seed via the Core's Simplex and ground
/// energy from the snapshot's sparse overrides — nothing here reimplements engine logic, and a live
/// frame (<c>world.ToSnapshot()</c>) and a loaded file produce identical scenes. The canvas draws
/// this; it holds no engine state of its own.
/// </summary>
public sealed class WorldScene
{
    private readonly TerrainSampler _terrain;
    private readonly GroundEnergyGrid _ground;
    private readonly SimulationConfig _config;
    private readonly bool _blightActive;
    private readonly double _temperatureOffset;

    private WorldScene(
        int width, int height, ColourMode mode, TerrainSampler terrain, GroundEnergyGrid ground,
        SimulationConfig config, bool blightActive, double temperatureOffset, bool plagueHatch,
        double globalLight, IReadOnlyList<OrganismView> organisms, OrganismFootprint? selectedFootprint)
    {
        Width = width;
        Height = height;
        Mode = mode;
        _terrain = terrain;
        _ground = ground;
        _config = config;
        _blightActive = blightActive;
        _temperatureOffset = temperatureOffset;
        PlagueHatch = plagueHatch;
        GlobalLight = globalLight;
        Organisms = organisms;
        SelectedFootprint = selectedFootprint;
    }

    public int Width { get; }

    public int Height { get; }

    public ColourMode Mode { get; }

    public bool PlagueHatch { get; }

    /// <summary>The frame's diurnal/seasonal light level (0 → 1), constant across the map this tick — for ambient screen tinting.</summary>
    public double GlobalLight { get; }

    public IReadOnlyList<OrganismView> Organisms { get; }

    public OrganismFootprint? SelectedFootprint { get; }

    /// <summary>The on-screen colour for tile (<paramref name="x"/>, <paramref name="y"/>) — biome × ground energy × event tint.</summary>
    public Color TileColour(int x, int y)
    {
        Biome biome = _terrain.BiomeAt(x, y);
        double cap = _config.Biomes.For(biome).EnergyCap;
        return BiomeColours.Tile(biome, _ground.EnergyAt(x, y), cap, _blightActive, _temperatureOffset);
    }

    public static WorldScene FromSnapshot(WorldSnapshot snapshot, ColourMode mode, long? selectedId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SimulationConfig config = snapshot.Configuration;
        var terrain = new TerrainSampler(snapshot.World.Seed, config);
        GroundEnergyGrid ground = GroundEnergyGrid.FromState(terrain, config, snapshot.GroundEnergy);
        var environment = new EnvironmentState(snapshot.EnvironmentModifiers);

        EnvironmentClock clock = EnvironmentClock.At(snapshot.Tick, config.Cycle);
        double temperatureOffset = environment.TemperatureOffset + clock.CyclicTemperatureOffset;
        bool eventActive = environment.Modifiers.Count > 0;
        double ceiling = Organism.EnergyCeiling;

        // The frame's diurnal/seasonal light, constant across the map this tick (spatial variation comes
        // from each tile's biome light factor). Used by the Light colour mode.
        double globalLight = clock.GlobalLight;

        var lineageByOrganism = new Dictionary<long, long>(snapshot.Lineages.Count);
        foreach (LineageSnapshot lineage in snapshot.Lineages)
        {
            lineageByOrganism[lineage.OrganismId] = lineage.LineageId;
        }

        var organisms = new List<OrganismView>(snapshot.Organisms.Count);
        OrganismFootprint? footprint = null;

        foreach (OrganismSnapshot organism in snapshot.Organisms)
        {
            long lineageId = lineageByOrganism.GetValueOrDefault(organism.OrganismId, organism.OrganismId);
            double tileTemperature = terrain.TemperatureCelsiusAt(organism.X, organism.Y) + temperatureOffset;
            double tileLight = globalLight * terrain.LightFactorAt(organism.X, organism.Y);
            Genome genome = organism.Genome.ToGenome();

            bool reproReady = ReproductionRules.Assess(
                genome, organism.Energy, organism.LastBirthTick, snapshot.Tick, config).IsReady;
            bool stressed = eventActive
                || Metabolism.ThermalStress(genome, tileTemperature, config.Metabolism) > 0.0;

            organisms.Add(new OrganismView
            {
                Id = organism.OrganismId,
                LineageId = lineageId,
                X = organism.X,
                Y = organism.Y,
                Radius = OrganismColours.Radius(genome.Size, config.TraitBounds.Size),
                Fill = OrganismColours.Fill(mode, organism, lineageId, ceiling, tileTemperature, tileLight),
                Outline = OrganismColours.Outline(organism.LastAction, organism.LastActionResult),
                ReproductiveReady = reproReady,
                Stressed = stressed,
                JustKilled = organism.LastActionResult == ActionResult.Killed,
            });

            if (selectedId == organism.OrganismId)
            {
                footprint = new OrganismFootprint(organism.X + 0.5, organism.Y + 0.5, genome.EnvRadius, genome.OrgRadius);
            }
        }

        return new WorldScene(
            snapshot.World.Width, snapshot.World.Height, mode, terrain, ground, config,
            environment.BlightActive, temperatureOffset, environment.PlagueActive, globalLight, organisms, footprint);
    }
}
