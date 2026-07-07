using LifeSim.Core.Neat;

namespace LifeSim.App.Presentation;

/// <summary>A placed NEAT node: normalized [0,1] position plus its live activation (node <c>state</c>).</summary>
public sealed record NeatNodeLayout(long Id, NodeType Type, double X, double Y, double Activation);

/// <summary>A placed NEAT connection, with endpoints and whether it is recurrent (feeds a same/earlier layer).</summary>
public sealed record NeatEdgeLayout(
    long FromId, long ToId, double Weight, bool Enabled, bool Recurrent,
    double FromX, double FromY, double ToX, double ToY);

/// <summary>A laid-out brain graph ready to draw. <see cref="Depth"/> is the effective feed-forward depth (columns from input to output).</summary>
public sealed record NeatGraph(IReadOnlyList<NeatNodeLayout> Nodes, IReadOnlyList<NeatEdgeLayout> Edges, int Depth = 1);

/// <summary>
/// Lays out a NEAT genome for the inspector's brain view so the <em>effective</em>
/// evolved network is visible: inputs on the left, outputs on the right, and hidden nodes placed by
/// their true feed-forward depth (longest enabled path from any input) rather than one flat middle
/// column — so accreted layers spread out and the network's depth reads at a glance. Live per-node
/// activations and weighted edges are carried through, and recurrent (cycle-creating) links are
/// flagged so the canvas can style them distinctly. Pure function of the genome.
/// </summary>
public static class NeatGraphLayout
{
    public static NeatGraph Build(NeatGenome genome)
    {
        ArgumentNullException.ThrowIfNull(genome);

        IReadOnlyDictionary<long, int> rank = FeedForwardRanks(genome);
        var typeOf = genome.Nodes.ToDictionary(n => n.Id, n => n.Type);

        int hiddenMax = genome.Nodes.Where(n => n.Type == NodeType.Hidden)
            .Select(n => rank.GetValueOrDefault(n.Id))
            .DefaultIfEmpty(0)
            .Max();
        hiddenMax = genome.Nodes.Any(n => n.Type == NodeType.Hidden) ? Math.Max(1, hiddenMax) : 0;
        int outputColumn = hiddenMax + 1; // outputs sit one column past the deepest hidden layer

        int Column(NodeGene node) => node.Type switch
        {
            NodeType.Input => 0,
            NodeType.Output => outputColumn,
            _ => Math.Clamp(rank.GetValueOrDefault(node.Id), 1, hiddenMax),
        };

        // Spread each column's nodes evenly down it, in ascending id order for a stable render.
        var byColumn = genome.Nodes
            .GroupBy(Column)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Id).ToList());
        var position = new Dictionary<long, (double X, double Y)>(genome.Nodes.Count);
        foreach ((int column, List<NodeGene> nodes) in byColumn)
        {
            double x = outputColumn == 0 ? 0.0 : (double)column / outputColumn;
            for (int i = 0; i < nodes.Count; i++)
            {
                double y = nodes.Count == 1 ? 0.5 : 0.08 + (0.84 * i / (nodes.Count - 1));
                position[nodes[i].Id] = (x, y);
            }
        }

        var columnOf = genome.Nodes.ToDictionary(n => n.Id, Column);
        List<NeatNodeLayout> layoutNodes = genome.Nodes
            .Select(n => new NeatNodeLayout(n.Id, n.Type, position[n.Id].X, position[n.Id].Y, n.State))
            .ToList();

        List<NeatEdgeLayout> layoutEdges = genome.Connections
            .Where(c => position.ContainsKey(c.From) && position.ContainsKey(c.To))
            .Select(c => new NeatEdgeLayout(
                c.From, c.To, c.Weight, c.Enabled,
                Recurrent: columnOf[c.To] <= columnOf[c.From],
                position[c.From].X, position[c.From].Y, position[c.To].X, position[c.To].Y))
            .ToList();

        return new NeatGraph(layoutNodes, layoutEdges, Math.Max(1, outputColumn));
    }

    /// <summary>
    /// Longest-path depth of each node over enabled edges (inputs at 0), by bounded relaxation so a
    /// recurrent cycle can't loop forever — the effective feed-forward layering of the evolved net.
    /// </summary>
    private static IReadOnlyDictionary<long, int> FeedForwardRanks(NeatGenome genome)
    {
        var rank = genome.Nodes.ToDictionary(n => n.Id, _ => 0);
        List<ConnectionGene> edges = genome.Connections
            .Where(c => c.Enabled && rank.ContainsKey(c.From) && rank.ContainsKey(c.To))
            .OrderBy(c => c.InnovationId)
            .ToList();
        var isInput = genome.Nodes.Where(n => n.Type == NodeType.Input).Select(n => n.Id).ToHashSet();

        // At most one pass per node is enough for the longest path in a DAG; the cap also bounds cycles.
        for (int pass = 0; pass < genome.Nodes.Count; pass++)
        {
            bool changed = false;
            foreach (ConnectionGene edge in edges)
            {
                if (isInput.Contains(edge.To))
                {
                    continue; // inputs are always the source column
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

        return rank;
    }
}
