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
    Intelligence,
    PreyCount,
}

/// <summary>One row of the live-organisms list: identity plus the sortable stats. PreyCount is lifetime kills.</summary>
public sealed record OrganismRow(long OrganismId, string Name, long Age, double CellCount, long Children, double Score, int BrainNodes, double Intelligence, long PreyCount);

/// <summary>
/// Builds the sortable list of currently-alive organisms for the Organisms sidebar tab. Score is the
/// same composite success measure as the leaderboard (weighted descendants + reproductive rate +
/// longevity; see <see cref="LineageScore"/>), using each organism's current age as its lifespan.
/// </summary>
public static class OrganismListBuilder
{
    public static IReadOnlyList<OrganismRow> Build(WorldSnapshot snapshot, OrganismSortKey key, bool ascending)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        (Dictionary<long, double> descendants, Dictionary<long, long> children) = LineageScore.Lineage(snapshot);

        var multicellular = snapshot.Configuration.Multicellular;
        IEnumerable<OrganismRow> rows = snapshot.Organisms.Select(o => new OrganismRow(
            o.OrganismId,
            o.Name,
            o.Age,
            Morphology.CellCount(o.Genome.ToGenome(), multicellular),
            children.GetValueOrDefault(o.OrganismId),
            LineageScore.Score(descendants.GetValueOrDefault(o.OrganismId), children.GetValueOrDefault(o.OrganismId), o.Age),
            o.BrainNodeCount ?? o.Brain.Nodes.Count,
            o.Intelligence ?? BrainIntelligence.Score(o.Brain),
            o.PredationWins));

        Func<OrganismRow, double> selector = key switch
        {
            OrganismSortKey.Age => r => r.Age,
            OrganismSortKey.CellCount => r => r.CellCount,
            OrganismSortKey.Children => r => r.Children,
            OrganismSortKey.BrainNodes => r => r.BrainNodes,
            OrganismSortKey.Intelligence => r => r.Intelligence,
            OrganismSortKey.PreyCount => r => r.PreyCount,
            _ => r => r.Score,
        };

        // Tie-break by id so the order is stable/deterministic.
        return (ascending
                ? rows.OrderBy(selector).ThenBy(r => r.OrganismId)
                : rows.OrderByDescending(selector).ThenBy(r => r.OrganismId))
            .ToList();
    }
}
