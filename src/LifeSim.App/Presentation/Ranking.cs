using LifeSim.Core.Naming;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Presentation;

/// <summary>
/// One row of the whole-simulation leaderboard. "Success" is a weighted descendant
/// score — <c>children + 0.5·grandchildren + 0.25·great-grandchildren</c> — tie-broken by lifespan
/// then id. Covers every organism that ever lived, alive or dead; all are named via the deterministic
/// <see cref="OrganismNamer"/> (names are a pure function of id, so dead organisms are named too).
/// </summary>
public sealed record RankingEntry(
    int Rank, long OrganismId, string Name, double Score, long Children, long Lifespan, int Generation, bool IsAlive);

/// <summary>Ranks every organism in the run (from the all-time lineage records) most-to-least successful.</summary>
public static class RankingBuilder
{
    public static IReadOnlyList<RankingEntry> Build(WorldSnapshot snapshot, int topN = 500)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Parent links, and the weighted descendant score computed by walking up to 3 ancestors from
        // each organism (O(n)): each organism adds 1 to its parent, 0.5 to its grandparent, 0.25 to
        // its great-grandparent — i.e. children + 0.5·grandchildren + 0.25·great-grandchildren.
        var parentOf = new Dictionary<long, long>();
        var directChildren = new Dictionary<long, long>();
        foreach (LineageSnapshot l in snapshot.Lineages)
        {
            if (l.ParentId is { } parent)
            {
                parentOf[l.OrganismId] = parent;
                directChildren[parent] = directChildren.GetValueOrDefault(parent) + 1;
            }
        }

        var score = new Dictionary<long, double>();
        foreach (LineageSnapshot l in snapshot.Lineages)
        {
            if (!parentOf.TryGetValue(l.OrganismId, out long p1))
            {
                continue;
            }

            score[p1] = score.GetValueOrDefault(p1) + 1.0;
            if (parentOf.TryGetValue(p1, out long p2))
            {
                score[p2] = score.GetValueOrDefault(p2) + 0.5;
                if (parentOf.TryGetValue(p2, out long p3))
                {
                    score[p3] = score.GetValueOrDefault(p3) + 0.25;
                }
            }
        }

        var living = snapshot.Organisms.Select(o => o.OrganismId).ToHashSet();
        long now = snapshot.Tick;

        return snapshot.Lineages
            .Select(l => new
            {
                Lineage = l,
                Score = score.GetValueOrDefault(l.OrganismId),
                Children = directChildren.GetValueOrDefault(l.OrganismId),
                Lifespan = Math.Max(0, (l.DeathTick ?? now) - l.BirthTick),
                Alive = living.Contains(l.OrganismId),
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
                x.Alive))
            .ToList();
    }
}
