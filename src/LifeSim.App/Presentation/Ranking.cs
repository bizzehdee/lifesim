using LifeSim.Core.Naming;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Presentation;

/// <summary>
/// One row of the whole-simulation leaderboard. "Success" is a composite of weighted descendants,
/// reproductive rate, and longevity (see <see cref="LineageScore"/>) — tie-broken by lifespan then id.
/// Covers every organism that ever lived, alive or dead; all are named via the deterministic
/// <see cref="OrganismNamer"/> (names are a pure function of id, so dead organisms are named too).
/// </summary>
public sealed record RankingEntry(
    int Rank, long OrganismId, string Name, double Score, long Children, long Lifespan, int Generation, bool IsAlive, double HelpGiven);

/// <summary>Ranks every organism in the run (from the all-time lineage records) most-to-least successful.</summary>
public static class RankingBuilder
{
    public static IReadOnlyList<RankingEntry> Build(WorldSnapshot snapshot, int topN = 500)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        (Dictionary<long, double> descendants, Dictionary<long, long> directChildren) = LineageScore.Lineage(snapshot);

        var living = snapshot.Organisms.Select(o => o.OrganismId).ToHashSet();
        // HelpGiven (indirect fitness) is a live-organism tally — dead organisms' is unknown, shown as 0.
        var helpGiven = snapshot.Organisms.ToDictionary(o => o.OrganismId, o => o.HelpGiven);
        long now = snapshot.Tick;

        return snapshot.Lineages
            .Select(l =>
            {
                long lifespan = Math.Max(0, (l.DeathTick ?? now) - l.BirthTick);
                long children = directChildren.GetValueOrDefault(l.OrganismId);
                return new
                {
                    Lineage = l,
                    Score = LineageScore.Score(descendants.GetValueOrDefault(l.OrganismId), children, lifespan),
                    Children = children,
                    Lifespan = lifespan,
                    Alive = living.Contains(l.OrganismId),
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Lifespan)
            .ThenBy(x => x.Lineage.OrganismId)
            .Take(topN)
            .Select((x, i) => new RankingEntry(
                i + 1,
                x.Lineage.OrganismId,
                OrganismNamer.Name(x.Lineage.OrganismId, snapshot.Configuration.Naming),
                x.Score,
                x.Children,
                x.Lifespan,
                x.Lineage.GenerationDepth,
                x.Alive,
                helpGiven.GetValueOrDefault(x.Lineage.OrganismId)))
            .ToList();
    }
}
