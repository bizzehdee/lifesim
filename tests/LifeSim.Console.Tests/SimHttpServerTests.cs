using System.Net;
using System.Net.Sockets;
using LifeSim.Console.Serve;
using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Console.Tests;

public class SimHttpServerTests
{
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static SnapshotService NewService(ulong seed) =>
        new(SimulationWorld.CreateGenesis(
            new WorldState { Seed = seed, Width = 32, Height = 32 },
            SimulationConfig.Default with { InitialPopulation = 10 }));

    [Fact]
    public async Task Http_servesSnapshotAndHealth_andAcceptsEditedSnapshotsBack()
    {
        var service = NewService(seed: 5);
        int port = FreePort();
        using var server = new SimHttpServer(service, port, TextWriter.Null);
        using var cts = new CancellationTokenSource();
        Task run = server.RunAsync(cts.Token);

        using var client = new HttpClient { BaseAddress = new Uri(server.Prefix) };

        try
        {
            Assert.Equal("ok", await client.GetStringAsync("health"));

            WorldSnapshot served = SnapshotSerializer.Load(await client.GetStringAsync("snapshot"));
            Assert.Equal(5UL, served.World.Seed);

            // POST an edited (different) world back; the served snapshot must reflect it.
            var edited = SimulationWorld.CreateGenesis(
                new WorldState { Seed = 99, Width = 32, Height = 32 },
                SimulationConfig.Default with { InitialPopulation = 5 });
            HttpResponseMessage ok = await client.PostAsync(
                "snapshot", new StringContent(SnapshotSerializer.Save(edited.ToSnapshot())));
            Assert.True(ok.IsSuccessStatusCode);

            WorldSnapshot afterImport = SnapshotSerializer.Load(await client.GetStringAsync("snapshot"));
            Assert.Equal(99UL, afterImport.World.Seed);

            // A malformed POST is rejected, not adopted.
            HttpResponseMessage bad = await client.PostAsync("snapshot", new StringContent("{ nope }"));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        finally
        {
            await cts.CancelAsync();
            await run;
        }
    }
}
