using System.Net;
using System.Net.Sockets;
using LifeSim.App.Engine;
using LifeSim.App.ViewModels;
using LifeSim.Console.Cli;
using LifeSim.Console.Serve;
using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.App.Tests;

public class SessionTests
{
    // A session with a world already created (the ctor now shows the setup screen instead of auto-creating).
    private static MainViewModel CreatedSession()
    {
        var session = new MainViewModel(liveEngine: true, autoStart: false, post: a => a());
        Assert.True(session.TryCreateWorld(42, 48, 48, SimulationConfig.Default with { InitialPopulation = 30 }, out _));
        return session;
    }

    private static SimulationWorld NewWorld(ulong seed = 42) =>
        SimulationWorld.CreateGenesis(
            new WorldState { Seed = seed, Width = 48, Height = 48 },
            SimulationConfig.Default with { InitialPopulation = 30 });

    private static bool SpinUntil(Func<bool> condition, int timeoutMs = 4000)
    {
        long limit = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < limit)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    [Fact]
    public void EngineRunner_emitsInitialFrame_thenPlayPauseStepControlTheTick()
    {
        long lastTick = -1;
        int frames = 0;
        using var runner = new EngineRunner(NewWorld(), snap =>
        {
            Interlocked.Increment(ref frames);
            Volatile.Write(ref lastTick, snap.Tick);
        });

        Assert.Equal(0, Volatile.Read(ref lastTick)); // initial frame at tick 0
        Assert.Equal(1, frames);

        runner.SetTicksPerSecond(100);
        runner.Play();
        Assert.True(SpinUntil(() => Volatile.Read(ref lastTick) >= 3), "Playing should advance the tick.");

        runner.Pause();
        Assert.True(SpinUntil(() => !runner.IsPlaying));
        Thread.Sleep(60);
        long paused = Volatile.Read(ref lastTick);
        Thread.Sleep(60);
        Assert.Equal(paused, Volatile.Read(ref lastTick)); // paused → tick frozen

        runner.Step();
        Assert.True(SpinUntil(() => Volatile.Read(ref lastTick) == paused + 1), "Step should advance exactly one tick.");
    }

    [Fact]
    public async Task StreamClient_fetchesFromSimServe_andPostsAnEditedSnapshotBack()
    {
        var service = new SnapshotService(NewWorld(seed: 5));
        int port = FreePort();
        using var server = new SimHttpServer(service, port, TextWriter.Null);
        using var cts = new CancellationTokenSource();
        Task run = server.RunAsync(cts.Token);

        try
        {
            using var client = new SnapshotStreamClient(server.Prefix);

            WorldSnapshot fetched = await client.FetchAsync();
            Assert.Equal(5UL, fetched.World.Seed);

            var edited = SimulationWorld.CreateGenesis(
                new WorldState { Seed = 99, Width = 48, Height = 48 },
                SimulationConfig.Default with { InitialPopulation = 5 }).ToSnapshot();
            await client.PostAsync(edited);

            WorldSnapshot afterPost = await client.FetchAsync();
            Assert.Equal(99UL, afterPost.World.Seed);
        }
        finally
        {
            await cts.CancelAsync();
            await run;
        }
    }

    [Fact]
    public void Session_liveMode_rendersInitialFrameAndStepsForward()
    {
        var session = CreatedSession();

        Assert.NotNull(session.World.Scene);
        Assert.Equal(0, session.World.Tick);

        session.Step();
        Assert.True(SpinUntil(() => session.World.Tick >= 1), "Step should advance the rendered world.");
        session.Dispose();
    }

    [Fact]
    public void Session_edit_appendsAnEditLogEntry_andAppliesTheChange()
    {
        var session = CreatedSession();
        long id = session.World.Snapshot!.Organisms[0].OrganismId;
        session.World.SelectedOrganismId = id;

        session.EditEnergy = 3.5;
        session.ApplyEdit();

        Assert.True(SpinUntil(() =>
            session.World.Snapshot!.EditLog.Count == 1 &&
            session.World.Snapshot!.Organisms.Single(o => o.OrganismId == id).Energy == 3.5));
        session.Dispose();
    }

    [Fact]
    public void Session_edit_forksATraceableBranchWithoutOverwritingTheOriginal()
    {
        var session = CreatedSession();
        WorldSnapshot root = session.World.Snapshot!;
        Assert.NotNull(root.BranchId);       // the fresh world is a rooted timeline
        Assert.Null(root.ParentSnapshotId);

        long id = root.Organisms[0].OrganismId;
        session.World.SelectedOrganismId = id;
        session.EditEnergy = 4.0;
        session.ApplyEdit();

        Assert.True(SpinUntil(() => session.World.Snapshot!.ParentSnapshotId == root.SnapshotId));
        WorldSnapshot branched = session.World.Snapshot!;
        Assert.NotEqual(root.BranchId, branched.BranchId);      // a new comparable timeline
        Assert.Equal(root.SnapshotId, branched.ParentSnapshotId); // traceable back to the original
        Assert.Single(branched.EditLog);
        session.Dispose();
    }

    [Fact]
    public void Session_saveThenLoad_roundTripsThroughAFile()
    {
        DirectoryInfo dir = Directory.CreateTempSubdirectory("lifesim-session");
        try
        {
            var session = CreatedSession();
            session.Step();
            SpinUntil(() => session.World.Tick >= 1);
            long tick = session.World.Tick;

            string path = Path.Combine(dir.FullName, "world.json");
            session.SaveTo(path);
            Assert.True(File.Exists(path));

            var reader = new MainViewModel(liveEngine: true, autoStart: false, post: a => a());
            reader.LoadFrom(path);
            Assert.Equal(tick, reader.World.Tick);
            session.Dispose();
            reader.Dispose();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Session_exchangesWorldsWithTheConsoleApp_inBothDirections()
    {
        DirectoryInfo dir = Directory.CreateTempSubdirectory("lifesim-exchange");
        try
        {
            // Console produces a world → the app loads and renders it.
            string genesis = Path.Combine(dir.FullName, "genesis.json");
            Assert.Equal(0, SimCli.Run(
                ["new", "--out", genesis, "--seed", "7", "--width", "48", "--height", "48", "--population", "20"],
                TextWriter.Null, TextWriter.Null));

            var session = new MainViewModel(liveEngine: true, autoStart: false, post: a => a());
            session.LoadFrom(genesis);
            Assert.Equal(0, session.World.Tick);
            Assert.True(session.World.Population > 0);

            // App saves a world → the console advances it (round-trips through the shared format).
            string fromApp = Path.Combine(dir.FullName, "from-app.json");
            session.SaveTo(fromApp);
            string advanced = Path.Combine(dir.FullName, "advanced.json");
            Assert.Equal(0, SimCli.Run(
                ["run", "--in", fromApp, "--out", advanced, "--ticks", "5"], TextWriter.Null, TextWriter.Null));

            Assert.Equal(5, SnapshotSerializer.Load(File.ReadAllText(advanced)).Tick);
            session.Dispose();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Session_showsSetupBeforeAWorldIsCreated()
    {
        var session = new MainViewModel(liveEngine: true, autoStart: false, post: a => a());
        Assert.False(session.HasWorld);
        Assert.Null(session.World.Scene);
    }

    [Fact]
    public void TryCreateWorld_honoursSeedDimensionsAndPopulation()
    {
        var session = new MainViewModel(liveEngine: true, autoStart: false, post: a => a());

        Assert.True(session.TryCreateWorld(42, 96, 64, SimulationConfig.Default with { InitialPopulation = 15 }, out _));
        Assert.True(session.HasWorld);

        WorldSnapshot snap = session.World.Snapshot!;
        Assert.Equal(42UL, snap.World.Seed);
        Assert.Equal(96, snap.World.Width);
        Assert.Equal(64, snap.World.Height);
        Assert.Equal(15, snap.Organisms.Count);
        session.Dispose();
    }

    [Fact]
    public void TryCreateWorld_rejectsInvalidDimensionsOrPopulation()
    {
        var session = new MainViewModel(liveEngine: true, autoStart: false, post: a => a());

        Assert.False(session.TryCreateWorld(1, 0, 0, SimulationConfig.Default with { InitialPopulation = 10 }, out _));
        Assert.False(session.HasWorld);

        Assert.False(session.TryCreateWorld(1, 10, 10, SimulationConfig.Default with { InitialPopulation = 1000 }, out _));
        Assert.False(session.HasWorld);
    }

    [Fact]
    public void CreateWorld_buildsFromSetupFields_includingTheFullConfigJson()
    {
        var session = new MainViewModel(liveEngine: true, autoStart: false, post: a => a());
        session.Seed = 7;
        session.Width = 40;
        session.Height = 40;
        session.Population = 12;

        // A starting property set through the full-config JSON (any constant is reachable this way).
        SimulationConfig config = SimulationConfig.Default with
        {
            Reproduction = SimulationConfig.Default.Reproduction with { ReproductionBaseCost = 5.5 },
        };
        session.ConfigJson = SnapshotSerializer.SaveConfig(config);

        session.CreateWorld();

        Assert.True(session.HasWorld);
        WorldSnapshot snap = session.World.Snapshot!;
        Assert.Equal(7UL, snap.World.Seed);
        Assert.Equal(12, snap.Organisms.Count);
        Assert.Equal(5.5, snap.Configuration.Reproduction.ReproductionBaseCost);
        session.Dispose();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
