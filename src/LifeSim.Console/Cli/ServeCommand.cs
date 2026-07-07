using LifeSim.Console.Serve;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;

namespace LifeSim.Console.Cli;

/// <summary>
/// <c>sim serve --in state.json [--port P] [--tps R] [--max-ticks N]</c> — runs the engine and
/// exposes it over HTTP/WebSocket so a UI (the Avalonia browser demo) can stream a live run and
/// post edits back (lifesim.md §1). Advances on a background loop at <c>--tps</c> ticks per second;
/// blocks until Ctrl+C, extinction, or <c>--max-ticks</c>.
/// </summary>
public static class ServeCommand
{
    public static int Execute(CommandLine args, TextWriter output)
    {
        string inPath = args.GetRequired("in");
        int port = args.GetInt("port", 8080);
        double tps = args.GetDouble("tps", 10.0);
        if (tps <= 0)
        {
            throw new CommandLineException("--tps must be > 0.");
        }

        long maxTicks = args.GetLong("max-ticks", -1);

        var world = SimulationWorld.FromSnapshot(SnapshotSerializer.Load(File.ReadAllText(inPath)));
        var service = new SnapshotService(world);

        using var cts = new CancellationTokenSource();
        System.Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        using var server = new SimHttpServer(service, port, output);
        Task engine = RunEngineAsync(service, tps, maxTicks, output, cts.Token);

        output.WriteLine($"Serving {inPath} on {server.Prefix}");
        output.WriteLine("  GET /snapshot | POST /snapshot | GET /metrics | GET /health | WS /stream");
        output.WriteLine(
            $"  advancing at {tps} ticks/s" +
            $"{(maxTicks >= 0 ? $", stopping at tick {maxTicks}" : string.Empty)}. Ctrl+C to stop.");

        server.RunAsync(cts.Token).GetAwaiter().GetResult();

        cts.Cancel();
        engine.GetAwaiter().GetResult();
        return 0;
    }

    private static async Task RunEngineAsync(
        SnapshotService service, double tps, long maxTicks, TextWriter output, CancellationToken cancellationToken)
    {
        int delayMs = Math.Max(1, (int)Math.Round(1000.0 / tps));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (maxTicks >= 0 && service.Tick >= maxTicks)
                {
                    break;
                }

                if (!service.AdvanceOnce())
                {
                    output.WriteLine($"serve: population extinct at tick {service.Tick}; engine halted (snapshots still served).");
                    break;
                }

                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested via Ctrl+C.
        }
    }
}
