namespace LifeSim.Core.Neat;

/// <summary>
/// Immutable array-based execution layout compiled from a NEAT topology. It maps sparse innovation
/// ids to dense node slots once, and pre-sorts incoming edges once; weights remain in the genome so
/// Hebbian learning can change them without recompiling the topology.
/// </summary>
internal sealed class NeatExecutionPlan
{
    private NeatExecutionPlan(
        NodeType[] nodeTypes,
        int[] inputOrdinals,
        NeatIncomingEdge[][] incoming,
        int[] outputNodeIndices)
    {
        NodeTypes = nodeTypes;
        InputOrdinals = inputOrdinals;
        Incoming = incoming;
        OutputNodeIndices = outputNodeIndices;
    }

    public NodeType[] NodeTypes { get; }
    public int[] InputOrdinals { get; }
    public NeatIncomingEdge[][] Incoming { get; }
    public int[] OutputNodeIndices { get; }

    public static NeatExecutionPlan Compile(NeatGenome genome)
    {
        var nodeIndex = new Dictionary<long, int>(genome.Nodes.Count);
        var nodeTypes = new NodeType[genome.Nodes.Count];
        var inputOrdinals = new int[genome.Nodes.Count];
        var incomingLists = new List<NeatIncomingEdge>[genome.Nodes.Count];

        for (int i = 0; i < genome.Nodes.Count; i++)
        {
            NodeGene node = genome.Nodes[i];
            nodeIndex[node.Id] = i;
            nodeTypes[i] = node.Type;
            inputOrdinals[i] = node.Type == NodeType.Input ? checked((int)node.Id) : -1;
            incomingLists[i] = [];
        }

        // Sorting once here preserves the evaluator's canonical floating-point reduction order.
        foreach ((ConnectionGene connection, int connectionIndex) in genome.Connections
                     .Select((connection, index) => (connection, index))
                     .Where(entry => entry.connection.Enabled)
                     .OrderBy(entry => entry.connection.InnovationId))
        {
            if (!nodeIndex.TryGetValue(connection.To, out int targetIndex))
            {
                continue;
            }

            int sourceIndex = nodeIndex.GetValueOrDefault(connection.From, -1);
            incomingLists[targetIndex].Add(new NeatIncomingEdge(sourceIndex, connectionIndex));
        }

        int[] outputNodeIndices = NeatTopology.OutputNodeIds
            .Select(id => nodeIndex.GetValueOrDefault(id, -1))
            .ToArray();

        return new NeatExecutionPlan(
            nodeTypes,
            inputOrdinals,
            incomingLists.Select(edges => edges.ToArray()).ToArray(),
            outputNodeIndices);
    }
}

internal readonly record struct NeatIncomingEdge(int SourceNodeIndex, int ConnectionIndex);
