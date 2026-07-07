namespace LifeSim.Console.Cli;

/// <summary>
/// Resolves the <c>--threads N</c> option shared by <c>run</c> and <c>serve</c> (lifesim.md §7). The
/// value is an execution knob only — the simulation is byte-identical for any thread count — clamped
/// to <c>[1, Environment.ProcessorCount]</c> (the machine's hardware threads).
/// </summary>
public static class ThreadOption
{
    public static int Resolve(CommandLine args)
    {
        ArgumentNullException.ThrowIfNull(args);
        int requested = args.GetInt("threads", 1);
        if (requested < 1)
        {
            throw new CommandLineException("--threads must be >= 1.");
        }

        return Math.Min(requested, Environment.ProcessorCount);
    }
}
