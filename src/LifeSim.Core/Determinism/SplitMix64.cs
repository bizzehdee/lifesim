namespace LifeSim.Core.Determinism;

/// <summary>
/// The SplitMix64 avalanche finalizer, shared wherever a seed, salt, or id needs to be
/// deterministically expanded or decorrelated without consuming any PRNG stream state
/// (used by <c>PrngStreams</c> stream derivation, <c>TerrainSampler</c> noise-layer seeding, /// and <c>OrganismNamer</c>).
/// </summary>
internal static class SplitMix64
{
    public static ulong Finalize(ulong x)
    {
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }
}
