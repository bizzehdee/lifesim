using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Presentation;

/// <summary>How the live-organisms list is ordered.</summary>
public enum OrganismSortKey
{
    Age,
    CellCount,
    Children,
    Score,
    BrainNodes,
}

/// <summary>One row of the live-organisms list: identity plus the sortable stats.</summary>
public sealed record OrganismRow(long OrganismId, string Name, long Age, double CellCount, long Children, double Score, int BrainNodes);

/// <summary>
/// Builds the sortable list of currently-alive organisms for the Organisms sidebar tab. Score is the
/// same weighted-descendant measure as the leaderboard (children + ½ grandchildren + ¼ great-grandchildren).
/// </summary>
public static class OrganismListBuilder
{
    public static IReadOnlyList<OrganismRow> Build(WorldSnapshot snapshot, OrganismSortKey key, bool ascending)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Direct children and weighted descendant score over the all-time lineage records (O(n)).
        var parentOf = new Dictionary<long, long>();
        var children = new Dictionary<long, long>();
        foreach (LineageSnapshot l in snapshot.Lineages)
        {
            if (l.ParentId is { } parent)
            {
                parentOf[l.OrganismId] = parent;
                children[parent] = children.GetValueOrDefault(parent) + 1;
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

        var multicellular = snapshot.Configuration.Multicellular;
        IEnumerable<OrganismRow> rows = snapshot.Organisms.Select(o => new OrganismRow(
            o.OrganismId,
            o.Name,
            o.Age,
            Morphology.CellCount(o.Genome.ToGenome(), multicellular),
            children.GetValueOrDefault(o.OrganismId),
            score.GetValueOrDefault(o.OrganismId),
            o.Brain.Nodes.Count));

        Func<OrganismRow, double> selector = key switch
        {
            OrganismSortKey.Age => r => r.Age,
            OrganismSortKey.CellCount => r => r.CellCount,
            OrganismSortKey.Children => r => r.Children,
            OrganismSortKey.BrainNodes => r => r.BrainNodes,
            _ => r => r.Score,
        };

        // Tie-break by id so the order is stable/deterministic.
        return (ascending
                ? rows.OrderBy(selector).ThenBy(r => r.OrganismId)
                : rows.OrderByDescending(selector).ThenBy(r => r.OrganismId))
            .ToList();
    }
}
