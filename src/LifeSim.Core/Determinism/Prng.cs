namespace LifeSim.Core.Determinism;

/// <summary>
/// Deterministic pseudo-random generator (xoshiro256** with SplitMix64 seeding).
/// State is four 64-bit words and is fully serializable, so a snapshot replays identically
///. All draws are pure integer/double operations — no wall-clock or ambient
/// entropy — so the same state always yields the same sequence on any platform.
/// </summary>
public sealed class Prng
{
    private ulong _s0, _s1, _s2, _s3;

    /// <summary>Create a generator seeded from a single 64-bit value.</summary>
    public Prng(ulong seed)
    {
        // SplitMix64 expands the scalar seed into the 256-bit state.
        ulong sm = seed;
        _s0 = ExpandSeed(ref sm);
        _s1 = ExpandSeed(ref sm);
        _s2 = ExpandSeed(ref sm);
        _s3 = ExpandSeed(ref sm);
        // xoshiro requires a non-zero state; SplitMix64 effectively never produces all-zero,
        // but guard anyway for total determinism.
        if ((_s0 | _s1 | _s2 | _s3) == 0)
        {
            _s0 = 1;
        }
    }

    private Prng(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
    }

    /// <summary>Restore a generator from previously captured state (exactly 4 words).</summary>
    public static Prng FromState(ReadOnlySpan<ulong> state)
    {
        if (state.Length != 4)
        {
            throw new ArgumentException("PRNG state must be exactly 4 words.", nameof(state));
        }

        return new Prng(state[0], state[1], state[2], state[3]);
    }

    /// <summary>Capture the full internal state (4 words) for serialization.</summary>
    public ulong[] GetState() => [_s0, _s1, _s2, _s3];

    /// <summary>Next raw 64-bit value.</summary>
    public ulong NextULong()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);

        return result;
    }

    /// <summary>Uniform double in [0, 1) using the top 53 bits (deterministic across platforms).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Uniform integer in [0, maxExclusive) with rejection sampling (no modulo bias).</summary>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);

        uint bound = (uint)maxExclusive;
        uint threshold = (uint)(-(int)bound) % bound; // (2^32 mod bound), rejection floor
        while (true)
        {
            uint r = (uint)NextULong();
            if (r >= threshold)
            {
                return (int)(r % bound);
            }
        }
    }

    /// <summary>
    /// Standard-normal sample via the Marsaglia polar method.
    /// NOTE: uses Math.Log/Sqrt; sqrt is IEEE-deterministic but log is not guaranteed
    /// bit-identical across platforms. This is the transcendental-determinism caveat called
    /// out (a candidate for the fixed-point migration). The cross-platform
    /// parity guarantee in Phase 1 covers the integer PRNG and Simplex, not Gaussian draws.
    /// </summary>
    public double NextGaussian()
    {
        double u, v, s;
        do
        {
            u = (2.0 * NextDouble()) - 1.0;
            v = (2.0 * NextDouble()) - 1.0;
            s = (u * u) + (v * v);
        }
        while (s >= 1.0 || s == 0.0);

        double factor = Math.Sqrt(-2.0 * Math.Log(s) / s);
        return u * factor;
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    private static ulong ExpandSeed(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        return SplitMix64.Finalize(x);
    }
}
