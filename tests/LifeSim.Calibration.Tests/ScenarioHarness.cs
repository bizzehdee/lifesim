using System.Globalization;
using LifeSim.Console.Cli;
using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Events;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Calibration.Tests;

/// <summary>
/// Drives calibration scenarios through the <c>sim</c> CLI (lifesim.md §15) — the console is the
/// harness the calibration suite runs the engine through (Phase 11). Builds worlds either from the
/// genesis command or from a hand-constructed snapshot, advances them via <c>sim run</c>, and
/// returns the resulting snapshot plus the per-tick population series (from the CSV metrics stream).
/// </summary>
public sealed class ScenarioHarness : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("lifesim-calib");
    private int _counter;

    public void Dispose() => _dir.Delete(recursive: true);

    /// <summary>Runs <c>sim new</c> for a default-config world and returns the genesis snapshot.</summary>
    public WorldSnapshot Genesis(ulong seed, int width, int height, int population)
    {
        string outPath = File($"genesis_{seed}.json");
        int code = SimCli.Run(
            [
                "new", "--out", outPath,
                "--seed", seed.ToString(CultureInfo.InvariantCulture),
                "--width", width.ToString(CultureInfo.InvariantCulture),
                "--height", height.ToString(CultureInfo.InvariantCulture),
                "--population", population.ToString(CultureInfo.InvariantCulture),
            ],
            TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);
        return SnapshotSerializer.Load(System.IO.File.ReadAllText(outPath));
    }

    /// <summary>Advances a snapshot <paramref name="ticks"/> ticks via <c>sim run</c>; returns the final snapshot and the per-tick metrics stream.</summary>
    public RunResult Run(WorldSnapshot initial, int ticks)
    {
        int id = _counter++;
        string inPath = File($"in_{id}.json");
        string outPath = File($"out_{id}.json");
        string metricsPath = File($"metrics_{id}.csv");
        System.IO.File.WriteAllText(inPath, SnapshotSerializer.Save(initial));

        int code = SimCli.Run(
            ["run", "--in", inPath, "--out", outPath, "--ticks", ticks.ToString(CultureInfo.InvariantCulture), "--metrics", metricsPath],
            TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        string[] lines = System.IO.File.ReadAllLines(metricsPath).Where(l => l.Length > 0).ToArray();
        string[] header = lines[0].Split(',');
        List<string[]> rows = lines.Skip(1).Select(l => l.Split(',')).ToList();
        return new RunResult(SnapshotSerializer.Load(System.IO.File.ReadAllText(outPath)), header, rows);
    }

    /// <summary>Assembles a hand-placed world snapshot for a controlled scenario.</summary>
    public static WorldSnapshot BuildWorld(
        WorldState world, SimulationConfig config, IReadOnlyList<Organism> organisms,
        IReadOnlyList<GroundEnergyEntry>? groundEnergy = null,
        IReadOnlyList<EnvironmentModifier>? modifiers = null)
    {
        long nextId = organisms.Count == 0 ? 0 : organisms.Max(o => o.Id) + 1;
        return new WorldSnapshot
        {
            World = world,
            Configuration = config,
            PrngStreams = PrngStreams.FromSeed(world.Seed).CaptureState(),
            EvolutionBookkeeping = new EvolutionBookkeeping { NextOrganismId = nextId },
            GroundEnergy = groundEnergy?.ToList() ?? [],
            Organisms = organisms.Select(OrganismSnapshot.From).ToList(),
            Lineages = organisms
                .Select(o => new LineageSnapshot { OrganismId = o.Id, LineageId = o.Id, BirthTick = 0, GenerationDepth = 0, BirthTraits = GenomeSnapshot.From(o.Genome) })
                .ToList(),
            Metrics = new SimulationMetrics { Population = organisms.Count, Extinct = false },
            EnvironmentModifiers = modifiers?.ToList() ?? [],
        };
    }

    public static Organism MakeOrganism(long id, Genome genome, double energy, int x, int y) =>
        new(id, genome, $"Calib-Test-Org{id}", energy, x, y,
            NeatGenomeFactory.CreateMinimalFullyConnected(new Prng((ulong)id + 1)));

    /// <summary>A stationary, sense-cheap genome used to isolate a single pressure (thermal, plague, blight) from movement/sensing noise.</summary>
    public static Genome InertGenome(double thermalCenter = 15.0, double thermalWidth = 21.0) => new()
    {
        Size = 2.0,
        SpeedCapacity = 0.0,
        ThermalCenter = thermalCenter,
        ThermalWidth = thermalWidth,
        EnvRadius = 0.0,
        OrgRadius = 0.0,
        SensoryAcuity = 1.0,
    };

    public static (int X, int Y) FindTile(TerrainSampler terrain, Biome biome, int extent = 96)
    {
        for (int y = 0; y < extent; y++)
        {
            for (int x = 0; x < extent; x++)
            {
                if (terrain.BiomeAt(x, y) == biome)
                {
                    return (x, y);
                }
            }
        }

        throw new InvalidOperationException($"No {biome} tile found within {extent}x{extent}.");
    }

    /// <summary>Zeroes a tile and its four orthogonal neighbours so no Harvest can add energy there.</summary>
    public static IReadOnlyList<GroundEnergyEntry> DrainedCross(int x, int y) =>
    [
        new(x, y, 0.0),
        new(x, y - 1, 0.0),
        new(x, y + 1, 0.0),
        new(x - 1, y, 0.0),
        new(x + 1, y, 0.0),
    ];

    private string File(string name) => Path.Combine(_dir.FullName, name);
}

/// <summary>The outcome of a <c>sim run</c>: the final snapshot plus the parsed per-tick CSV metrics stream.</summary>
public sealed record RunResult(WorldSnapshot Final, string[] Header, IReadOnlyList<string[]> Rows)
{
    public IReadOnlyList<long> Populations => Column("population");

    public IReadOnlyList<long> Column(string name)
    {
        int index = Array.IndexOf(Header, name);
        return Rows.Select(r => long.Parse(r[index], CultureInfo.InvariantCulture)).ToList();
    }

    public long Total(string name) => Column(name).Sum();
}
