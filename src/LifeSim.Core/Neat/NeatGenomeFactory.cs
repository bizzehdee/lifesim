using LifeSim.Core.Determinism;

namespace LifeSim.Core.Neat;

/// <summary>
/// Builds the genesis brain: a minimal fully-connected network — every input wired directly to
/// every output, no hidden nodes, random weights. Canonical NEAT starting point;
/// structure only grows under real selective pressure (Phase 8).
/// </summary>
public static class NeatGenomeFactory
{
    /// <summary>Weights are drawn uniformly from [-1, 1] via the genesis PRNG stream.</summary>
    public static NeatGenome CreateMinimalFullyConnected(Prng genesisStream)
    {
        ArgumentNullException.ThrowIfNull(genesisStream);

        var nodes = new List<NodeGene>(NeatTopology.InputCount + NeatTopology.OutputCount);
        foreach (long id in NeatTopology.InputNodeIds)
        {
            nodes.Add(new NodeGene { Id = id, Type = NodeType.Input });
        }

        foreach (long id in NeatTopology.OutputNodeIds)
        {
            nodes.Add(new NodeGene { Id = id, Type = NodeType.Output });
        }

        var connections = new List<ConnectionGene>(NeatTopology.InputCount * NeatTopology.OutputCount);
        for (int i = 0; i < NeatTopology.InputCount; i++)
        {
            for (int j = 0; j < NeatTopology.OutputCount; j++)
            {
                double weight = (genesisStream.NextDouble() * 2.0) - 1.0;
                connections.Add(new ConnectionGene
                {
                    InnovationId = NeatTopology.ConnectionInnovationId(i, j),
                    From = NeatTopology.InputNodeIds[i],
                    To = NeatTopology.OutputNodeIds[j],
                    Weight = weight,
                    Enabled = true,
                });
            }
        }

        return new NeatGenome { Nodes = nodes, Connections = connections };
    }
}
