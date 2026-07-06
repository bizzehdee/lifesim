using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
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
}
