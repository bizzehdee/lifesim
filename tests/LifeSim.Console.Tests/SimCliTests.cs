using System.Text.Json;
using LifeSim.Console.Cli;
using LifeSim.Core.Configuration;
using LifeSim.Core.Snapshot;

namespace LifeSim.Console.Tests;

public class SimCliTests
{
    private sealed class TempDir : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("lifesim-cli");

        public string Path => _dir.FullName;

        public string File(string name) => System.IO.Path.Combine(_dir.FullName, name);

        public void Dispose() => _dir.Delete(recursive: true);
    }

    private static int RunCli(out string output, out string error, params string[] args)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        int code = SimCli.Run(args, outWriter, errWriter);
        output = outWriter.ToString();
        error = errWriter.ToString();
        return code;
    }

    [Fact]
    public void New_createsALoadableGenesisSnapshot()
    {
        using var temp = new TempDir();
        string state = temp.File("state.json");

        int code = RunCli(out _, out _, "new", "--out", state,
            "--seed", "42", "--width", "32", "--height", "32", "--population", "10");

        Assert.Equal(0, code);
        WorldSnapshot snapshot = SnapshotSerializer.Load(File.ReadAllText(state));
        Assert.Equal(0, snapshot.Tick);
        Assert.Equal(42UL, snapshot.World.Seed);
        Assert.Equal(10, snapshot.Organisms.Count);
    }

    [Fact]
    public void Run_advancesTheWorldAndWritesTheFinalSnapshot()
    {
        using var temp = new TempDir();
        string genesis = temp.File("genesis.json");
        string final = temp.File("final.json");
        RunCli(out _, out _, "new", "--out", genesis, "--seed", "7", "--width", "32", "--height", "32", "--population", "15");

        int code = RunCli(out _, out _, "run", "--in", genesis, "--out", final, "--ticks", "20");

        Assert.Equal(0, code);
        Assert.Equal(20, SnapshotSerializer.Load(File.ReadAllText(final)).Tick);
    }

    [Fact]
    public void Run_isDeterministic_forTheSameSeed()
    {
        using var temp = new TempDir();

        string RunOnce(string tag)
        {
            string genesis = temp.File($"g{tag}.json");
            string final = temp.File($"f{tag}.json");
            RunCli(out _, out _, "new", "--out", genesis, "--seed", "2024", "--width", "40", "--height", "40", "--population", "25");
            RunCli(out _, out _, "run", "--in", genesis, "--out", final, "--ticks", "50");
            return File.ReadAllText(final);
        }

        Assert.Equal(RunOnce("a"), RunOnce("b"));
    }

    [Fact]
    public void Run_resumeFromMidpoint_matchesAnUninterruptedRun()
    {
        using var temp = new TempDir();
        string genesis = temp.File("genesis.json");
        RunCli(out _, out _, "new", "--out", genesis, "--seed", "909090", "--width", "48", "--height", "48", "--population", "25");

        string straight = temp.File("straight.json");
        RunCli(out _, out _, "run", "--in", genesis, "--out", straight, "--ticks", "100");

        string mid = temp.File("mid.json");
        string resumed = temp.File("resumed.json");
        RunCli(out _, out _, "run", "--in", genesis, "--out", mid, "--ticks", "50");
        RunCli(out _, out _, "run", "--in", mid, "--out", resumed, "--ticks", "50");

        // The console is the determinism harness: a run split across CLI invocations must be
        // byte-identical to one straight-through run.
        Assert.Equal(File.ReadAllText(straight), File.ReadAllText(resumed));
    }

    [Fact]
    public void Run_withThreads_isByteIdenticalToASerialRun()
    {
        using var temp = new TempDir();
        string genesis = temp.File("genesis.json");
        RunCli(out _, out _, "new", "--out", genesis, "--seed", "909090", "--width", "48", "--height", "48", "--population", "25");

        string serial = temp.File("serial.json");
        string threaded = temp.File("threaded.json");
        RunCli(out _, out _, "run", "--in", genesis, "--out", serial, "--ticks", "100", "--threads", "1");
        int exit = RunCli(out _, out _, "run", "--in", genesis, "--out", threaded, "--ticks", "100", "--threads", "4");

        // --threads is an execution knob only: the result must match a serial run.
        Assert.Equal(0, exit);
        Assert.Equal(File.ReadAllText(serial), File.ReadAllText(threaded));
    }

    [Fact]
    public void Run_withStream_writesOnePeriodicFramePerInterval()
    {
        using var temp = new TempDir();
        string genesis = temp.File("genesis.json");
        RunCli(out _, out _, "new", "--out", genesis, "--seed", "5", "--width", "32", "--height", "32", "--population", "10");

        string frames = Path.Combine(temp.Path, "frames");
        RunCli(out _, out _, "run", "--in", genesis, "--out", temp.File("final.json"),
            "--ticks", "20", "--out-dir", frames, "--stream", "5");

        // Ticks 5, 10, 15, 20 → 4 frames.
        Assert.Equal(4, Directory.GetFiles(frames, "frame_*.json").Length);
        Assert.True(File.Exists(Path.Combine(frames, "frame_00000020.json")));
    }

    [Fact]
    public void Run_withCsvMetrics_writesAHeaderAndOneRowPerTick()
    {
        using var temp = new TempDir();
        string genesis = temp.File("genesis.json");
        RunCli(out _, out _, "new", "--out", genesis, "--seed", "5", "--width", "32", "--height", "32", "--population", "10");

        string metrics = temp.File("metrics.csv");
        RunCli(out _, out _, "run", "--in", genesis, "--out", temp.File("final.json"), "--ticks", "10", "--metrics", metrics);

        string[] lines = File.ReadAllLines(metrics);
        Assert.Equal(11, lines.Length); // header + 10 ticks (population survives this run)
        Assert.StartsWith("tick,population", lines[0]);
    }

    [Fact]
    public void Run_withNdjsonMetrics_writesOneJsonLinePerTick()
    {
        using var temp = new TempDir();
        string genesis = temp.File("genesis.json");
        RunCli(out _, out _, "new", "--out", genesis, "--seed", "5", "--width", "32", "--height", "32", "--population", "10");

        string metrics = temp.File("metrics.ndjson");
        RunCli(out _, out _, "run", "--in", genesis, "--out", temp.File("final.json"),
            "--ticks", "10", "--metrics", metrics, "--metrics-format", "ndjson");

        string[] lines = File.ReadAllLines(metrics).Where(l => l.Length > 0).ToArray();
        Assert.Equal(10, lines.Length);
        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal(1, doc.RootElement.GetProperty("tick").GetInt64());
    }

    [Fact]
    public void Experiment_runsPairedSeedPanel_andWritesUncertaintyReport()
    {
        using var temp = new TempDir();
        string candidate = temp.File("candidate.json");
        string report = temp.File("report.json");
        File.WriteAllText(candidate, SnapshotSerializer.SaveConfig(SimulationConfig.Default));

        int code = RunCli(out string output, out string error,
            "experiment", "--candidate", candidate, "--out", report,
            "--seeds", "1,2,3,4,5", "--ticks", "2", "--width", "64", "--height", "64",
            "--population", "8", "--threads", "1");

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("5 seeds", output, StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(report));
        JsonElement root = json.RootElement;
        Assert.Equal(5, root.GetProperty("seeds").GetArrayLength());
        Assert.Equal(5, root.GetProperty("baseline").GetProperty("runs").GetArrayLength());
        Assert.Equal(0.0, root.GetProperty("comparison").GetProperty("final_population_difference").GetProperty("mean").GetDouble());
        Assert.Equal("inconclusive_final_population", root.GetProperty("comparison").GetProperty("conclusion").GetString());
    }

    [Fact]
    public void Experiment_rejectsSingleSeedConclusions()
    {
        using var temp = new TempDir();
        string candidate = temp.File("candidate.json");
        File.WriteAllText(candidate, SnapshotSerializer.SaveConfig(SimulationConfig.Default));

        int code = RunCli(out _, out string error,
            "experiment", "--candidate", candidate, "--out", temp.File("report.json"),
            "--seeds", "42", "--ticks", "1");

        Assert.Equal(2, code);
        Assert.Contains("at least 5 distinct seeds", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_printsUsageAndSucceeds()
    {
        int code = RunCli(out string output, out _, "help");
        Assert.Equal(0, code);
        Assert.Contains("Usage: sim", output, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCommand_reportsErrorWithNonZeroExit()
    {
        int code = RunCli(out _, out string error, "frobnicate");
        Assert.Equal(2, code);
        Assert.Contains("Unknown command", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_missingRequiredOption_reportsErrorWithNonZeroExit()
    {
        int code = RunCli(out _, out string error, "run", "--in", "nowhere.json");
        Assert.Equal(2, code);
        Assert.Contains("--out", error, StringComparison.Ordinal);
    }
}
