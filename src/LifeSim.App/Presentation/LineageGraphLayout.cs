using LifeSim.Core.Naming;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Presentation;

/// <summary>A placed organism in a lineage tree: normalized [0,1] position, generation, and status.</summary>
public sealed record LineageGraphNode(
    long OrganismId, string Name, int Generation, bool IsAlive, bool IsFocus, double X, double Y);

/// <summary>A parent → child edge in the lineage tree, with endpoints.</summary>
public sealed record LineageGraphEdge(double FromX, double FromY, double ToX, double ToY);

/// <summary>A laid-out lineage/family tree ready to draw, plus the focused organism.</summary>
public sealed record LineageGraph(IReadOnlyList<LineageGraphNode> Nodes, IReadOnlyList<LineageGraphEdge> Edges, long FocusId, bool Truncated);

/// <summary>
/// Lays out a focus-centred slice of an organism's lineage: its direct
/// ancestor line up to <paramref name="maxParentGenerations"/> generations, and its descendant
/// subtree down to <paramref name="maxChildGenerations"/> generations. Nodes are placed by generation
/// (rows) with subtrees grouped via a depth-first ordering (columns); the focus is flagged. A hard
/// node cap guards pathological trees. Pure function of the snapshot.
/// </summary>
public static class LineageGraphLayout
{
    public static LineageGraph Build(
        WorldSnapshot snapshot, long focusId, int maxParentGenerations = 3, int maxChildGenerations = 32, int maxNodes = 2000)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        LineageSnapshot? focus = snapshot.Lineages.FirstOrDefault(l => l.OrganismId == focusId);
        if (focus is null)
        {
            return new LineageGraph([], [], focusId, Truncated: false);
        }

        long lineageId = focus.LineageId;
        Dictionary<long, LineageSnapshot> byId = snapshot.Lineages
            .Where(l => l.LineageId == lineageId)
            .ToDictionary(l => l.OrganismId);

        var childrenOf = new Dictionary<long, List<long>>();
        foreach (LineageSnapshot l in byId.Values)
        {
            if (l.ParentId is { } p && byId.ContainsKey(p))
            {
                (childrenOf.TryGetValue(p, out List<long>? kids) ? kids : childrenOf[p] = []).Add(l.OrganismId);
            }
        }

        // Focus's direct ancestor line (up), then its descendant subtree (down), each bounded.
        var included = new HashSet<long> { focusId };
        bool truncated = false;

        long? ancestor = byId[focusId].ParentId;
        for (int up = 1; up <= maxParentGenerations && ancestor is { } a && byId.ContainsKey(a); up++)
        {
            included.Add(a);
            ancestor = byId[a].ParentId;
        }

        var frontier = new Queue<(long Id, int Depth)>();
        frontier.Enqueue((focusId, 0));
        while (frontier.Count > 0)
        {
            (long id, int depth) = frontier.Dequeue();
            if (depth >= maxChildGenerations || !childrenOf.TryGetValue(id, out List<long>? kids))
            {
                continue;
            }

            foreach (long child in kids)
            {
                if (included.Count >= maxNodes)
                {
                    truncated = true;
                    break;
                }

                if (included.Add(child))
                {
                    frontier.Enqueue((child, depth + 1));
                }
            }

            if (truncated)
            {
                break;
            }
        }

        // Depth-first pre-order from the topmost included nodes so subtrees stay grouped in a column.
        List<long> roots = included
            .Where(id => byId[id].ParentId is not { } p || !included.Contains(p))
            .OrderBy(x => x).ToList();
        var order = new Dictionary<long, int>();
        var stack = new Stack<long>(roots.AsEnumerable().Reverse());
        int counter = 0;
        while (stack.Count > 0)
        {
            long id = stack.Pop();
            if (!included.Contains(id) || !order.TryAdd(id, counter))
            {
                continue;
            }

            counter++;
            if (childrenOf.TryGetValue(id, out List<long>? kids))
            {
                foreach (long child in kids.OrderByDescending(x => x))
                {
                    stack.Push(child);
                }
            }
        }

        foreach (long id in included.Where(id => !order.ContainsKey(id)))
        {
            order[id] = counter++;
        }

        // Position: y by generation relative to the included band (topmost row = 0), x by DFS order.
        Dictionary<int, List<long>> levels = included
            .GroupBy(id => byId[id].GenerationDepth)
            .ToDictionary(g => g.Key, g => g.OrderBy(id => order[id]).ToList());
        int minDepth = levels.Keys.Min();
        int maxDepth = levels.Keys.Max();
        int span = maxDepth - minDepth;

        var pos = new Dictionary<long, (double X, double Y)>();
        foreach ((int depth, List<long> ids) in levels)
        {
            double y = span == 0 ? 0.5 : (double)(depth - minDepth) / span;
            for (int i = 0; i < ids.Count; i++)
            {
                double x = ids.Count == 1 ? 0.5 : (double)i / (ids.Count - 1);
                pos[ids[i]] = (x, y);
            }
        }

        var living = snapshot.Organisms.Select(o => o.OrganismId).ToHashSet();
        List<LineageGraphNode> nodes = included
            .Select(id => new LineageGraphNode(
                id, OrganismNamer.Name(id, snapshot.Configuration.Naming), byId[id].GenerationDepth,
                living.Contains(id), id == focusId, pos[id].X, pos[id].Y))
            .ToList();

        var edges = new List<LineageGraphEdge>();
        foreach (long id in included)
        {
            if (byId[id].ParentId is { } parent && included.Contains(parent))
            {
                edges.Add(new LineageGraphEdge(pos[parent].X, pos[parent].Y, pos[id].X, pos[id].Y));
            }
        }

        return new LineageGraph(nodes, edges, focusId, truncated);
    }
}
