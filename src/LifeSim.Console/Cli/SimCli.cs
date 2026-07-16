using LifeSim.Core;
using LifeSim.Core.Snapshot;

namespace LifeSim.Console.Cli;

/// <summary>
/// The <c>sim</c> command dispatcher. Kept separate from the process entry point so
/// the whole CLI surface is directly unit-testable with in-memory writers and temp files — this is
/// the harness the determinism and calibration suites drive the engine through.
/// </summary>
public static class SimCli
{
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        CommandLine cli;
        try
        {
            cli = CommandLine.Parse(args);
        }
        catch (CommandLineException ex)
        {
            error.WriteLine(ex.Message);
            return 2;
        }

        try
        {
            return cli.Command switch
            {
                "new" => NewCommand.Execute(cli, output),
                "run" => RunCommand.Execute(cli, output),
                "experiment" => ExperimentCommand.Execute(cli, output),
                "serve" => ServeCommand.Execute(cli, output),
                "help" or "--help" or "-h" => PrintUsage(output),
                "" => PrintUsage(error, exitCode: 1),
                _ => Unknown(cli.Command, error),
            };
        }
        catch (CommandLineException ex)
        {
            error.WriteLine(ex.Message);
            return 2;
        }
        catch (SnapshotValidationException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            return 1;
        }
        catch (FileNotFoundException ex)
        {
            error.WriteLine($"error: file not found: {ex.FileName ?? ex.Message}");
            return 1;
        }
        catch (DirectoryNotFoundException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string command, TextWriter error)
    {
        error.WriteLine($"Unknown command '{command}'. Try 'sim help'.");
        return 2;
    }

    private static int PrintUsage(TextWriter writer, int exitCode = 0)
    {
        writer.WriteLine($"LifeSim engine {BuildInfo.SimulationVersion} " +
            $"(schema {BuildInfo.SchemaVersion}, config {BuildInfo.ConfigVersion})");
        writer.WriteLine();
        writer.WriteLine("Usage: sim <command> [options]");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine("  new    --out FILE [--seed S] [--width W] [--height H] [--population P] [--config FILE]");
        writer.WriteLine("         Create an initial world from config + seed.");
        writer.WriteLine("  run    --in FILE --out FILE --ticks N [--out-dir DIR --stream K]");
        writer.WriteLine("         [--metrics FILE --metrics-format csv|ndjson] [--threads N]");
        writer.WriteLine("         Advance the world N ticks; optionally stream frames and metrics.");
        writer.WriteLine("  experiment --candidate CONFIG --out REPORT [--baseline CONFIG]");
        writer.WriteLine("         [--seeds 1,2,3,4,5] [--ticks N] [--width W --height H --population P]");
        writer.WriteLine("         Run a paired multi-seed comparison with 95% intervals (minimum 5 seeds).");
        writer.WriteLine("  serve  --in FILE [--port P] [--tps R] [--max-ticks N] [--threads N]");
        writer.WriteLine("         Run the engine and expose snapshots over HTTP/WebSocket.");
        writer.WriteLine();
        writer.WriteLine("  --threads N  per-tick worker threads for the parallel phases (sensing, brain");
        writer.WriteLine("               evaluation, metabolism), 1..hardware-threads (default: half the");
        writer.WriteLine("               hardware threads; execution-only, results are identical).");
        return exitCode;
    }
}
