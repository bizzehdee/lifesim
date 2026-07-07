using LifeSim.Console.Cli;

// The `sim` CLI entry point (lifesim.md §1). All real logic lives in SimCli/commands so the surface
// is unit-testable; this just forwards the process argv and standard streams.
return SimCli.Run(args, System.Console.Out, System.Console.Error);
