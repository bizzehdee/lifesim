using LifeSim.Core.Neat;

namespace LifeSim.Core.Tests;

public class NeatCrossoverTests
{
    private static NodeGene Node(long id, NodeType type = NodeType.Hidden) =>
        new() { Id = id, Type = type };

    private static ConnectionGene Conn(long innovation, long from, long to, double weight, bool enabled = true) =>
        new() { InnovationId = innovation, From = from, To = to, Weight = weight, Enabled = enabled };

    [Fact]
    public void Recombine_matchingConnections_averagesTheWeights()
    {
        var a = new NeatGenome
        {
            Nodes = [Node(0, NodeType.Input), Node(1, NodeType.Output)],
            Connections = [Conn(10, 0, 1, 2.0)],
        };
        var b = new NeatGenome
        {
            Nodes = [Node(0, NodeType.Input), Node(1, NodeType.Output)],
            Connections = [Conn(10, 0, 1, 4.0)],
        };

        NeatGenome child = NeatCrossover.Recombine(a, b);

        ConnectionGene shared = Assert.Single(child.Connections);
        Assert.Equal(10, shared.InnovationId);
        Assert.Equal(3.0, shared.Weight); // (2 + 4) / 2
    }

    [Fact]
    public void Recombine_disjointAndExcessGenes_takesTheUnionOfBothParents()
    {
        var a = new NeatGenome
        {
            Nodes = [Node(0, NodeType.Input), Node(1, NodeType.Output)],
            Connections = [Conn(10, 0, 1, 1.0), Conn(11, 0, 0, 1.0)],
        };
        var b = new NeatGenome
        {
            Nodes = [Node(0, NodeType.Input), Node(1, NodeType.Output), Node(2)],
            Connections = [Conn(10, 0, 1, 1.0), Conn(12, 0, 2, 1.0)],
        };

        NeatGenome child = NeatCrossover.Recombine(a, b);

        // Matching (10) + a's excess (11) + b's disjoint (12) = union of both.
        Assert.Equal([10L, 11L, 12L], child.Connections.Select(c => c.InnovationId));
        // Node 2 (only in b) is inherited too.
        Assert.Equal([0L, 1L, 2L], child.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Recombine_enabledFlag_isTrueIfEitherParentEnablesIt()
    {
        var a = new NeatGenome { Connections = [Conn(10, 0, 1, 1.0, enabled: false)] };
        var bEnabled = new NeatGenome { Connections = [Conn(10, 0, 1, 1.0, enabled: true)] };
        var bDisabled = new NeatGenome { Connections = [Conn(10, 0, 1, 1.0, enabled: false)] };

        Assert.True(NeatCrossover.Recombine(a, bEnabled).Connections.Single().Enabled);
        Assert.False(NeatCrossover.Recombine(a, bDisabled).Connections.Single().Enabled);
    }

    [Fact]
    public void Recombine_output_isSortedByInnovationAndNodeId()
    {
        var a = new NeatGenome
        {
            Nodes = [Node(5), Node(1, NodeType.Input)],
            Connections = [Conn(30, 1, 5, 1.0), Conn(10, 1, 5, 1.0)],
        };
        var b = new NeatGenome
        {
            Nodes = [Node(3)],
            Connections = [Conn(20, 1, 3, 1.0)],
        };

        NeatGenome child = NeatCrossover.Recombine(a, b);

        Assert.Equal([1L, 3L, 5L], child.Nodes.Select(n => n.Id));
        Assert.Equal([10L, 20L, 30L], child.Connections.Select(c => c.InnovationId));
    }

    [Fact]
    public void Recombine_isDeterministic_regardlessOfParentOrder()
    {
        var a = new NeatGenome
        {
            Nodes = [Node(0, NodeType.Input), Node(1, NodeType.Output)],
            Connections = [Conn(10, 0, 1, 2.0), Conn(11, 0, 0, 3.0)],
        };
        var b = new NeatGenome
        {
            Nodes = [Node(0, NodeType.Input), Node(1, NodeType.Output), Node(2)],
            Connections = [Conn(10, 0, 1, 6.0), Conn(12, 0, 2, 5.0)],
        };

        NeatGenome first = NeatCrossover.Recombine(a, b);
        NeatGenome second = NeatCrossover.Recombine(a, b);
        Assert.Equal(first, second); // repeatable

        // Order independence for the shared gene's weight (average is commutative) and the topology set.
        NeatGenome swapped = NeatCrossover.Recombine(b, a);
        Assert.Equal(
            first.Connections.Select(c => c.InnovationId),
            swapped.Connections.Select(c => c.InnovationId));
        Assert.Equal(
            first.Connections.Single(c => c.InnovationId == 10).Weight,
            swapped.Connections.Single(c => c.InnovationId == 10).Weight);
        Assert.Equal(first.Nodes.Select(n => n.Id), swapped.Nodes.Select(n => n.Id));
    }
}
