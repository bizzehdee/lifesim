using LifeSim.Core.Neat;

namespace LifeSim.App.Presentation;

/// <summary>
/// A display-only "cognition" index in [0, 100] summarising how sophisticated an organism's brain is.
/// Purely for ranking / observability — it never feeds back into the simulation.
/// <para>
/// It is computed over the <b>functional</b> network only: nodes and enabled connections that lie on an
/// enabled path from an input to an output, so vestigial gene-fragments left by NEAT node-splits don't
/// inflate it. Four axes are combined, each squashed by a saturating curve <c>s(x,k)=x/(x+k)</c> so no
/// single axis runs away as brains grow:
/// </para>
/// <list type="bullet">
///   <item><b>Depth</b> — effective feed-forward layers (compositional computation); weighted highest.</item>
///   <item><b>Hidden units</b> — functional internal processing nodes.</item>
///   <item><b>Wiring</b> — functional enabled connections (parameter capacity).</item>
///   <item><b>Memory</b> — functional recurrent edges (temporal integration).</item>
/// </list>
/// It is a capacity ceiling, not realised intelligence (the only true measure of that is surviving
/// descendants) — but it tracks brain sophistication and is a stable, pure function of the brain.
/// </summary>
public static class BrainIntelligence
{
    // Half-max constants for s(x,k)=x/(x+k): the axis reads 0.5 when its value equals k. Tuned near a
    // mature brain's typical values.
    private const double DepthK = 4.0;
    private const double HiddenK = 16.0;
    private const double WiringK = 150.0;
    private const double MemoryK = 10.0;

    // Axis weights (sum to 1): depth dominates, then breadth of units, then wiring and memory.
    private const double DepthWeight = 0.45;
    private const double HiddenWeight = 0.25;
    private const double WiringWeight = 0.15;
    private const double MemoryWeight = 0.15;

    public static double Score(NeatGenome brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        BrainShape shape = Analyse(brain);
        double index =
            (DepthWeight * Saturate(shape.Depth, DepthK))
            + (HiddenWeight * Saturate(shape.HiddenUnits, HiddenK))
            + (WiringWeight * Saturate(shape.Wiring, WiringK))
            + (MemoryWeight * Saturate(shape.RecurrentEdges, MemoryK));
        return 100.0 * index;
    }

    private static double Saturate(double x, double k) => x <= 0.0 ? 0.0 : x / (x + k);

    private readonly record struct BrainShape(int Depth, int HiddenUnits, int Wiring, int RecurrentEdges);

    private static BrainShape Analyse(NeatGenome brain)
    {
        var nodeType = new Dictionary<long, NodeType>(brain.Nodes.Count);
        foreach (NodeGene node in brain.Nodes)
        {
            nodeType[node.Id] = node.Type;
        }

        // Enabled edges whose endpoints both exist.
        List<ConnectionGene> edges = brain.Connections
            .Where(c => c.Enabled && nodeType.ContainsKey(c.From) && nodeType.ContainsKey(c.To))
            .OrderBy(c => c.InnovationId)
            .ToList();

        var forwardAdj = new Dictionary<long, List<long>>();
        var backwardAdj = new Dictionary<long, List<long>>();
        foreach (ConnectionGene e in edges)
        {
            Append(forwardAdj, e.From, e.To);
            Append(backwardAdj, e.To, e.From);
        }

        var inputs = nodeType.Where(kv => kv.Value == NodeType.Input).Select(kv => kv.Key).ToList();
        var outputs = nodeType.Where(kv => kv.Value == NodeType.Output).Select(kv => kv.Key).ToList();

        // Functional = reachable from an input AND able to reach an output.
        HashSet<long> forward = Reach(inputs, forwardAdj);
        HashSet<long> backward = Reach(outputs, backwardAdj);
        forward.IntersectWith(backward);
        HashSet<long> functional = forward;

        List<ConnectionGene> functionalEdges = edges
            .Where(e => functional.Contains(e.From) && functional.Contains(e.To))
            .ToList();

        int hiddenUnits = functional.Count(id => nodeType[id] == NodeType.Hidden);

        // Longest-path feed-forward rank over functional edges (inputs at 0), bounded so a recurrent
        // cycle can't loop forever — the effective layering of the evolved net.
        var rank = new Dictionary<long, int>(functional.Count);
        foreach (long id in functional)
        {
            rank[id] = 0;
        }

        for (int pass = 0; pass < functional.Count; pass++)
        {
            bool changed = false;
            foreach (ConnectionGene e in functionalEdges)
            {
                if (nodeType[e.To] == NodeType.Input)
                {
                    continue; // inputs stay at rank 0
                }

                int candidate = rank[e.From] + 1;
                if (candidate > rank[e.To])
                {
                    rank[e.To] = candidate;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        int depth = rank.Count == 0 ? 0 : rank.Values.Max();
        int recurrent = functionalEdges.Count(e => rank[e.To] <= rank[e.From]);

        return new BrainShape(depth, hiddenUnits, functionalEdges.Count, recurrent);
    }

    private static void Append(Dictionary<long, List<long>> adjacency, long from, long to)
    {
        if (!adjacency.TryGetValue(from, out List<long>? list))
        {
            list = [];
            adjacency[from] = list;
        }

        list.Add(to);
    }

    private static HashSet<long> Reach(IEnumerable<long> seeds, Dictionary<long, List<long>> adjacency)
    {
        var seen = new HashSet<long>();
        var stack = new Stack<long>(seeds);
        while (stack.Count > 0)
        {
            long node = stack.Pop();
            if (!seen.Add(node))
            {
                continue;
            }

            if (adjacency.TryGetValue(node, out List<long>? next))
            {
                foreach (long m in next)
                {
                    if (!seen.Contains(m))
                    {
                        stack.Push(m);
                    }
                }
            }
        }

        return seen;
    }
}
