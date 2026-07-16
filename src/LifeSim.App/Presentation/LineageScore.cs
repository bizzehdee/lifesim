using LifeSim.Core.Snapshot;

namespace LifeSim.App.Presentation;

/// <summary>
/// The shared "success" score for the leaderboard and the live-organisms list, so both rank identically.
/// It builds on the weighted descendant count — <c>children + ½·grandchildren + ¼·great-grandchildren</c>,
/// each halved again if that descendant was itself a dead end (never had offspring of its own) — but also
/// rewards <em>reproductive rate</em> and <em>longevity</em>:
/// <code>
/// score = W_descendants·D
///       + W_rate·(directChildren · Window / max(lifespan, 1))
///       + W_longevity·(lifespan / Window)
/// </code>
/// The consequences, by design:
/// <list type="bullet">
///   <item>Offspring dominate — one child is worth far more than any plausible amount of pure lifespan
///     (<see cref="LongevityWeight"/> is small), so the ranking still reads much like the old
///     descendant-only score.</item>
///   <item>Producing a brood <em>faster</em> is worth more than the same brood produced slowly: the rate
///     term scales with children-per-tick, so e.g. 10 young in 50 ticks outscores 15 in 200.</item>
///   <item>With reproduction equal, a longer life outscores a shorter one (the longevity term).</item>
/// </list>
/// Pure and deterministic — a function of the all-time lineage records plus the current tick.
/// </summary>
public static class LineageScore
{
    /// <summary>Weight on the weighted descendant count (the historical score); 1.0 keeps that scale.</summary>
    public const double DescendantWeight = 1.0;

    /// <summary>Weight on reproductive rate (children per <see cref="LifespanWindow"/> ticks).</summary>
    public const double RateWeight = 1.0;

    /// <summary>Weight on longevity (lifespans, in windows). Small, so lifespan alone never beats offspring.</summary>
    public const double LongevityWeight = 0.1;

    /// <summary>Reference time window (ticks) that normalises the rate and longevity terms.</summary>
    public const double LifespanWindow = 100.0;

    /// <summary>A descendant that never had offspring of its own is a dead end — it counts for half.</summary>
    public const double DeadEndMultiplier = 0.5;

    /// <summary>
    /// Per-organism weighted descendants (D) and direct-child counts, from the all-time lineage records
    /// (O(n): each organism credits 1.0 to its parent, 0.5 to its grandparent, 0.25 to its
    /// great-grandparent — halved again at every level if it was itself childless, per
    /// <see cref="DeadEndMultiplier"/>).
    /// </summary>
    public static (Dictionary<long, double> WeightedDescendants, Dictionary<long, long> DirectChildren) Lineage(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

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

        var descendants = new Dictionary<long, double>();
        foreach (LineageSnapshot l in snapshot.Lineages)
        {
            if (!parentOf.TryGetValue(l.OrganismId, out long p1))
            {
                continue;
            }

            double weight = directChildren.ContainsKey(l.OrganismId) ? 1.0 : DeadEndMultiplier;
            descendants[p1] = descendants.GetValueOrDefault(p1) + weight;
            if (parentOf.TryGetValue(p1, out long p2))
            {
                descendants[p2] = descendants.GetValueOrDefault(p2) + (0.5 * weight);
                if (parentOf.TryGetValue(p2, out long p3))
                {
                    descendants[p3] = descendants.GetValueOrDefault(p3) + (0.25 * weight);
                }
            }
        }

        return (descendants, directChildren);
    }

    /// <summary>The composite success score for one organism (see the class summary for the formula).</summary>
    public static double Score(double weightedDescendants, long directChildren, long lifespan)
    {
        double rate = directChildren * LifespanWindow / Math.Max(lifespan, 1L);
        return (DescendantWeight * weightedDescendants)
            + (RateWeight * rate)
            + (LongevityWeight * (lifespan / LifespanWindow));
    }
}
