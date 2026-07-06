using LifeSim.Core;

// Phase 0 scaffold: the `sim` CLI. Real subcommands (run / serve / new) arrive in Phase 11.
// Proves the Console -> Core reference is wired end to end.
Console.WriteLine($"LifeSim engine {BuildInfo.SimulationVersion} " +
    $"(schema {BuildInfo.SchemaVersion}, config {BuildInfo.ConfigVersion})");
Console.WriteLine("CLI subcommands (run/serve/new) not implemented yet — see tasks.md Phase 11.");
return 0;
