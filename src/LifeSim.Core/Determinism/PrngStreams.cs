using System.Collections.Generic;

namespace LifeSim.Core.Determinism;

/// <summary>
/// The full set of named deterministic streams for a world, each independently seeded from the
/// master seed. The complete state of every stream is serialized into each snapshot (lifesim.md
/// §9, §12) — not just the master seed — so a run resumes bit-identically.
/// </summary>
public sealed class PrngStreams
{
    private static readonly PrngStream[] AllStreams =
        (PrngStream[])Enum.GetValues<PrngStream>();

    private readonly Dictionary<PrngStream, Prng> _streams;

    private PrngStreams(Dictionary<PrngStream, Prng> streams) => _streams = streams;

    /// <summary>Create fresh streams from a master seed. Each stream gets a distinct derived seed.</summary>
    public static PrngStreams FromSeed(ulong masterSeed)
    {
        var streams = new Dictionary<PrngStream, Prng>(AllStreams.Length);
        foreach (PrngStream stream in AllStreams)
        {
            streams[stream] = new Prng(DeriveSeed(masterSeed, stream));
        }

        return new PrngStreams(streams);
    }

    /// <summary>Rehydrate streams from serialized per-stream state (keyed by stream name).</summary>
    public static PrngStreams FromState(IReadOnlyDictionary<string, ulong[]> state)
    {
        var streams = new Dictionary<PrngStream, Prng>(AllStreams.Length);
        foreach (PrngStream stream in AllStreams)
        {
            if (!state.TryGetValue(stream.ToString(), out ulong[]? words))
            {
                throw new ArgumentException($"Missing PRNG state for stream '{stream}'.", nameof(state));
            }

            streams[stream] = Prng.FromState(words);
        }

        return new PrngStreams(streams);
    }

    /// <summary>Access a specific stream.</summary>
    public Prng this[PrngStream stream] => _streams[stream];

    /// <summary>Capture the full state of every stream for serialization, keyed by stream name.</summary>
    public Dictionary<string, ulong[]> CaptureState()
    {
        var state = new Dictionary<string, ulong[]>(_streams.Count);
        foreach (PrngStream stream in AllStreams)
        {
            state[stream.ToString()] = _streams[stream].GetState();
        }

        return state;
    }

    // Mix the master seed with the stream ordinal so streams are decorrelated yet reproducible.
    private static ulong DeriveSeed(ulong masterSeed, PrngStream stream)
    {
        ulong x = masterSeed + ((ulong)stream + 1) * 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }
}
