using System.Globalization;
using LifeSim.Core.Metrics;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;

namespace LifeSim.Console.Cli;

/// <summary>
/// <c>sim run --in state.json --out state.json --ticks N [--out-dir ./frames --stream K]
/// [--metrics file --metrics-format csv|ndjson]</c> — advances the world N ticks and writes the
/// resulting snapshot, optionally emitting periodic frame snapshots and a per-tick metrics stream
///. A run halts early if the population goes extinct.
/// </summary>
public static class RunCommand
{
    public static int Execute(CommandLine args, TextWriter output)
    {
        string inPath = args.GetRequired("in");
        string outPath = args.GetRequired("out");
        long ticks = args.GetLong("ticks", 0);
        if (ticks < 0)
        {
            throw new CommandLineException("--ticks must be >= 0.");
        }

        string? outDir = args.GetString("out-dir");
        int stream = args.GetInt("stream", 0);
        if (outDir is not null && stream <= 0)
        {
            throw new CommandLineException("--out-dir requires --stream K with K > 0.");
        }

        string? metricsPath = args.GetString("metrics");
        string metricsFormat = (args.GetString("metrics-format") ?? "csv").ToLowerInvariant();
        if (metricsFormat is not ("csv" or "ndjson"))
        {
            throw new CommandLineException("--metrics-format must be 'csv' or 'ndjson'.");
        }

        var world = SimulationWorld.FromSnapshot(SnapshotSerializer.Load(File.ReadAllText(inPath)));
        world.MaxDegreeOfParallelism = ThreadOption.Resolve(args);

        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
        }

        using StreamWriter? metrics = metricsPath is null ? null : new StreamWriter(metricsPath, append: false);
        if (metrics is not null && metricsFormat == "csv")
        {
            metrics.WriteLine(MetricsExporter.CsvHeader());
        }

        long executed = 0;
        for (long i = 0; i < ticks && !world.Extinct; i++)
        {
            world.Advance();
            executed++;

            if (metrics is not null)
            {
                metrics.WriteLine(metricsFormat == "csv"
                    ? MetricsExporter.CsvRow(world.Tick, world.Metrics)
                    : MetricsExporter.NdjsonLine(world.Tick, world.Metrics));
            }

            if (outDir is not null && world.Tick % stream == 0)
            {
                WriteFrame(outDir, world);
            }
        }

        File.WriteAllText(outPath, SnapshotSerializer.Save(world.ToSnapshot()));
        output.WriteLine(
            $"Advanced {executed}/{ticks} ticks (tick={world.Tick}, population={world.Organisms.Count}" +
            $"{(world.Extinct ? ", extinct" : string.Empty)}) -> {outPath}");
        return 0;
    }

    private static void WriteFrame(string outDir, SimulationWorld world)
    {
        string frame = Path.Combine(outDir, $"frame_{world.Tick.ToString("D8", CultureInfo.InvariantCulture)}.json");
        File.WriteAllText(frame, SnapshotSerializer.Save(world.ToSnapshot()));
    }
}
