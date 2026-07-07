using LifeSim.Core.Neat;

namespace LifeSim.App.Presentation;

/// <summary>A placed NEAT node: normalized [0,1] position plus its live activation (node <c>state</c>).</summary>
public sealed record NeatNodeLayout(long Id, NodeType Type, double X, double Y, double Activation);

/// <summary>A placed NEAT connection, with endpoints and whether it is recurrent (feeds a same/earlier layer).</summary>
public sealed record NeatEdgeLayout(
    long FromId, long ToId, double Weight, bool Enabled, bool Recurrent,
    double FromX, double FromY, double ToX, double ToY);

/// <summary>A laid-out brain graph ready to draw.</summary>
public sealed record NeatGraph(IReadOnlyList<NeatNodeLayout> Nodes, IReadOnlyList<NeatEdgeLayout> Edges);

/// <summary>
/// Lays out a NEAT genome for the inspector's brain view (lifesim.md §18): inputs in a left column,
/// outputs in a right column, hidden nodes in the middle, with live per-node activations and weighted
/// edges. Recurrent (cycle-creating) links are flagged so the canvas can style them distinctly from
/// feed-forward ones. Pure function of the genome — no engine mutation.
/// </summary>
public static class NeatGraphLayout
{
    public static NeatGraph Build(NeatGenome genome)
    {
        ArgumentNullException.ThrowIfNull(genome);

        var layerOf = new Dictionary<long, int>(genome.Nodes.Count);
        foreach (NodeGene node in genome.Nodes)
        {
            layerOf[node.Id] = Layer(node.Type);
        }

        // Spread each layer's nodes evenly down its column, in ascending id order for stability.
        var byLayer = genome.Nodes
            .GroupBy(n => Layer(n.Type))
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Id).ToList());
        var position = new Dictionary<long, (double X, double Y)>(genome.Nodes.Count);
        foreach ((int layer, List<NodeGene> nodes) in byLayer)
        {
            double x = layer / 2.0; // layers 0,1,2 → x 0, 0.5, 1
            for (int i = 0; i < nodes.Count; i++)
            {
                double y = nodes.Count == 1 ? 0.5 : 0.08 + (0.84 * i / (nodes.Count - 1));
                position[nodes[i].Id] = (x, y);
            }
        }

        List<NeatNodeLayout> layoutNodes = genome.Nodes
            .Select(n => new NeatNodeLayout(n.Id, n.Type, position[n.Id].X, position[n.Id].Y, n.State))
            .ToList();

        List<NeatEdgeLayout> layoutEdges = genome.Connections
            .Where(c => position.ContainsKey(c.From) && position.ContainsKey(c.To))
            .Select(c => new NeatEdgeLayout(
                c.From, c.To, c.Weight, c.Enabled,
                Recurrent: layerOf[c.To] <= layerOf[c.From],
                position[c.From].X, position[c.From].Y, position[c.To].X, position[c.To].Y))
            .ToList();

        return new NeatGraph(layoutNodes, layoutEdges);
    }

    private static int Layer(NodeType type) => type switch
    {
        NodeType.Input => 0,
        NodeType.Hidden => 1,
        NodeType.Output => 2,
        _ => 1,
    };
}
