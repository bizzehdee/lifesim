using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Naming;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Determinism.Tests;

/// <summary>
/// Determinism foundations (lifesim.md §9). The two flagship tests — Seed Replay and Save/Reload
/// Equivalence — arrive in Phase 4 once the tick loop exists; these guard the primitives they rely on.
/// </summary>
public class FoundationDeterminismTests
{
    [Fact]
    public void Streams_fromSameSeed_areIdentical()
    {
        var a = PrngStreams.FromSeed(2024);
        var b = PrngStreams.FromSeed(2024);

        foreach (PrngStream stream in Enum.GetValues<PrngStream>())
        {
            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(a[stream].NextULong(), b[stream].NextULong());
            }
        }
    }

    [Fact]
    public void Streams_areDecorrelated()
    {
        var streams = PrngStreams.FromSeed(2024);
        // Distinct streams should not start in lock-step.
        ulong genesis = streams[PrngStream.Genesis].NextULong();
        ulong behavior = streams[PrngStream.Behavior].NextULong();
        ulong mutation = streams[PrngStream.Mutation].NextULong();
        Assert.False(genesis == behavior && behavior == mutation);
    }

    [Fact]
    public void SnapshotPrngState_roundTripsExactly_thenContinuesIdentically()
    {
        var streams = PrngStreams.FromSeed(7);

        // Advance streams unevenly so the captured state is non-trivial.
        for (int i = 0; i < 100; i++)
        {
            streams[PrngStream.Behavior].NextULong();
        }
        for (int i = 0; i < 37; i++)
        {
            streams[PrngStream.Mutation].NextULong();
        }

        var snapshot = new WorldSnapshot
        {
            Tick = 500,
            World = new WorldState { Seed = 7, Width = 128, Height = 128 },
            Configuration = SimulationConfig.Default,
            PrngStreams = streams.CaptureState(),
            EvolutionBookkeeping = new EvolutionBookkeeping { NextInnovationId = 3, NextOrganismId = 9 },
        };

        // Serialize -> deserialize -> restore, exactly as a resume would.
        WorldSnapshot loaded = SnapshotSerializer.Load(SnapshotSerializer.Save(snapshot));
        var restored = PrngStreams.FromState(loaded.PrngStreams);

        foreach (PrngStream stream in Enum.GetValues<PrngStream>())
        {
            for (int i = 0; i < 200; i++)
            {
                Assert.Equal(streams[stream].NextULong(), restored[stream].NextULong());
            }
        }
    }

    [Fact]
    public void Simplex_sameSeed_producesIdenticalGrid()
    {
        // Transcendental-free simplex is bit-deterministic; running this suite under the WASM
        // build validates desktop/WASM parity (lifesim.md §1). Here we pin same-input determinism.
        var a = new SimplexNoise(31337);
        var b = new SimplexNoise(31337);
        var config = NoiseConfig.Default;

        for (int gx = 0; gx < 64; gx++)
        {
            for (int gy = 0; gy < 64; gy++)
            {
                Assert.Equal(a.SampleFractal(gx, gy, config), b.SampleFractal(gx, gy, config));
            }
        }
    }

    [Fact]
    public void BiomeMap_sameSeed_isByteIdentical()
    {
        // Phase 2 exit criteria (lifesim.md §2, §12): given a seed, the Core reconstructs a
        // byte-identical biome map — no terrain layout is ever stored, only the seed is.
        var config = SimulationConfig.Default;
        var a = new TerrainSampler(2024, config);
        var b = new TerrainSampler(2024, config);

        for (int y = 0; y < 96; y++)
        {
            for (int x = 0; x < 96; x++)
            {
                Assert.Equal(a.BiomeAt(x, y), b.BiomeAt(x, y));
                Assert.Equal(a.MoistureAt(x, y), b.MoistureAt(x, y));
                Assert.Equal(a.TemperatureAt(x, y), b.TemperatureAt(x, y));
            }
        }
    }

    [Fact]
    public void GroundEnergy_regeneratesToCap_andNeverExceedsIt()
    {
        var config = SimulationConfig.Default;
        var terrain = new TerrainSampler(2024, config);
        var grid = new GroundEnergyGrid(terrain, config);

        // Grassland regenerates a finite amount per tick (lifesim.md §2); a fully-drained tile
        // must climb back to exactly its cap and never overshoot, however long it's given.
        int x = 0;
        while (terrain.BiomeAt(x, 0) != Biome.Grassland)
        {
            x++;
        }

        double cap = grid.CapAt(x, 0);
        grid.Drain(x, 0, cap);

        for (int tick = 0; tick < 5_000; tick++)
        {
            grid.RegenerateTick();
            Assert.True(grid.EnergyAt(x, 0) <= cap);
        }

        Assert.Equal(cap, grid.EnergyAt(x, 0));
    }

    [Fact]
    public void OrganismNaming_isPureFunctionOfIdAndWordListVersion_consumesNoPrngDraw()
    {
        // lifesim.md §9/§19: names are a pure function of organism_id + word-list version and
        // must not disturb any PRNG stream.
        var config = new NamingConfig();
        var streams = PrngStreams.FromSeed(2024);
        ulong[] before = streams[PrngStream.Genesis].GetState();

        string first = OrganismNamer.Name(777, config);
        string second = OrganismNamer.Name(777, config);

        Assert.Equal(first, second);
        Assert.Equal(before, streams[PrngStream.Genesis].GetState());
    }
}
