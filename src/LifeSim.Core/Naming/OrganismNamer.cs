using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;

namespace LifeSim.Core.Naming;

/// <summary>
/// Deterministic organism naming: <c>name = f(organism_id, wordlist_version)</c> (lifesim.md
/// §19). A pure hash of the id is split into two adjective indices and one noun index — no
/// registry, no per-tick state, and no PRNG draw, so it reproduces identically on replay.
/// </summary>
public static class OrganismNamer
{
    public static string Name(long organismId, NamingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        WordList adjectives = WordList.Load(config.AdjectiveListVersion);
        WordList nouns = WordList.Load(config.NounListVersion);

        ulong hash = SplitMix64.Finalize((ulong)organismId);
        int adjective1 = (int)(hash % (ulong)adjectives.Count);

        hash = SplitMix64.Finalize(hash);
        int adjective2 = (int)(hash % (ulong)adjectives.Count);

        hash = SplitMix64.Finalize(hash);
        int noun = (int)(hash % (ulong)nouns.Count);

        if (config.RequireDistinctAdjectives && adjective2 == adjective1)
        {
            adjective2 = (adjective2 + 1) % adjectives.Count;
        }

        return $"{adjectives[adjective1]}-{adjectives[adjective2]}-{nouns[noun]}";
    }
}
