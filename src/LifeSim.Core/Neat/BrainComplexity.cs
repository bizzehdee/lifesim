namespace LifeSim.Core.Neat;

/// <summary>Display-only functional-network complexity score in [0, 100].</summary>
public static class BrainComplexity
{
    private const double DepthK = 4.0;
    private const double HiddenK = 16.0;
    private const double WiringK = 150.0;
    private const double MemoryK = 10.0;

    public static double Score(NeatGenome brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        BrainShape shape = Analyse(brain);
        double index =
            (0.45 * Saturate(shape.Depth, DepthK))
            + (0.25 * Saturate(shape.HiddenUnits, HiddenK))
            + (0.15 * Saturate(shape.Wiring, WiringK))
            + (0.15 * Saturate(shape.RecurrentEdges, MemoryK));
        return 100.0 * index;
    }

    private static double Saturate(double value, double midpoint) =>
        value <= 0.0 ? 0.0 : value / (value + midpoint);

    private static BrainShape Analyse(NeatGenome brain)
    {
        Dictionary<long, NodeType> nodeType = brain.Nodes.ToDictionary(node => node.Id, node => node.Type);
        List<ConnectionGene> edges = brain.Connections
            .Where(connection => connection.Enabled
                && nodeType.ContainsKey(connection.From)
                && nodeType.ContainsKey(connection.To))
            .OrderBy(connection => connection.InnovationId)
            .ToList();

        var forwardAdjacency = new Dictionary<long, List<long>>();
        var backwardAdjacency = new Dictionary<long, List<long>>();
        foreach (ConnectionGene edge in edges)
        {
            Append(forwardAdjacency, edge.From, edge.To);
            Append(backwardAdjacency, edge.To, edge.From);
        }

        HashSet<long> functional = Reach(
            nodeType.Where(entry => entry.Value == NodeType.Input).Select(entry => entry.Key),
            forwardAdjacency);
        functional.IntersectWith(Reach(
            nodeType.Where(entry => entry.Value == NodeType.Output).Select(entry => entry.Key),
            backwardAdjacency));
        List<ConnectionGene> functionalEdges = edges
            .Where(edge => functional.Contains(edge.From) && functional.Contains(edge.To))
            .ToList();

        var rank = functional.ToDictionary(id => id, _ => 0);
        for (int pass = 0; pass < functional.Count; pass++)
        {
            bool changed = false;
            foreach (ConnectionGene edge in functionalEdges)
            {
                if (nodeType[edge.To] == NodeType.Input)
                {
                    continue;
                }

                int candidate = rank[edge.From] + 1;
                if (candidate > rank[edge.To])
                {
                    rank[edge.To] = candidate;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        int depth = rank.Count == 0 ? 0 : rank.Values.Max();
        return new BrainShape(
            depth,
            functional.Count(id => nodeType[id] == NodeType.Hidden),
            functionalEdges.Count,
            functionalEdges.Count(edge => rank[edge.To] <= rank[edge.From]));
    }

    private static void Append(Dictionary<long, List<long>> adjacency, long from, long to)
    {
        if (!adjacency.TryGetValue(from, out List<long>? targets))
        {
            targets = [];
            adjacency[from] = targets;
        }

        targets.Add(to);
    }

    private static HashSet<long> Reach(IEnumerable<long> seeds, Dictionary<long, List<long>> adjacency)
    {
        var reached = new HashSet<long>();
        var pending = new Stack<long>(seeds);
        while (pending.TryPop(out long node))
        {
            if (!reached.Add(node) || !adjacency.TryGetValue(node, out List<long>? targets))
            {
                continue;
            }

            foreach (long target in targets)
            {
                pending.Push(target);
            }
        }

        return reached;
    }

    private readonly record struct BrainShape(int Depth, int HiddenUnits, int Wiring, int RecurrentEdges);
}
