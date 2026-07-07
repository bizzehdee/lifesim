using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;

namespace LifeSim.Console.Cli;

/// <summary>
/// <c>sim new --out state.json [--seed S] [--width W] [--height H] [--population P] [--config file]</c>
/// — creates an initial (tick 0) world from config + seed and writes it as a snapshot (lifesim.md §1, §17).
/// </summary>
public static class NewCommand
{
    public static int Execute(CommandLine args, TextWriter output)
    {
        string outPath = args.GetRequired("out");
        ulong seed = args.GetULong("seed", 1);
        int width = args.GetInt("width", 128);
        int height = args.GetInt("height", 128);

        SimulationConfig config = SimulationConfig.Default;
        string? configPath = args.GetString("config");
        if (configPath is not null)
        {
            config = SnapshotSerializer.LoadConfig(File.ReadAllText(configPath));
        }

        // --population overrides whatever the config (or default) specified.
        config = config with { InitialPopulation = args.GetInt("population", config.InitialPopulation) };

        var world = SimulationWorld.CreateGenesis(
            new WorldState { Seed = seed, Width = width, Height = height }, config);

        File.WriteAllText(outPath, SnapshotSerializer.Save(world.ToSnapshot()));
        output.WriteLine(
            $"Created genesis world: seed={seed} {width}x{height} population={world.Organisms.Count} -> {outPath}");
        return 0;
    }
}
