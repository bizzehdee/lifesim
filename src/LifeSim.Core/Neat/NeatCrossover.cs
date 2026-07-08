namespace LifeSim.Core.Neat;

/// <summary>
/// Biparental recombination of two NEAT germlines for sexual reproduction, aligning genes by
/// innovation id (classic NEAT crossover). Pure and PRNG-free:
/// <list type="bullet">
///   <item>Matching connection genes (same <see cref="ConnectionGene.InnovationId"/> in both parents)
///     take the <em>mean</em> of the two weights, and are enabled if either parent has them enabled.</item>
///   <item>Disjoint and excess genes (present in only one parent) are inherited from that parent — the
///     union of both parents' topology, keeping the offspring an even 50/50 mix (with no fitness
///     function there is no "fitter parent" to prefer structure from).</item>
///   <item>Node genes are the union of both node sets. Because innovation ids are allocated from a
///     single monotonic counter, a shared id descends from a shared allocation, so matching nodes are
///     homologous (same type/activation) and matching connections share the same <c>(From, To)</c>.</item>
/// </list>
/// Node <see cref="NodeGene.State"/> is irrelevant here (offspring brains are reset at birth). Output
/// is deterministically ordered — nodes by id, connections by innovation id — so replay and save/reload
/// stay bit-identical. The usual <see cref="NeatMutator"/> pass runs on the result afterwards (the
/// "little randomness"); this function itself never draws randomness.
/// </summary>
public static class NeatCrossover
{
    public static NeatGenome Recombine(NeatGenome a, NeatGenome b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        // Node genes: union by id. Matching ids are homologous, so either parent's copy is equivalent;
        // seed with b, then let a win on overlap (deterministic, and irrelevant since they match).
        var nodes = new Dictionary<long, NodeGene>();
        foreach (NodeGene node in b.Nodes)
        {
            nodes[node.Id] = node;
        }

        foreach (NodeGene node in a.Nodes)
        {
            nodes[node.Id] = node;
        }

        // Connection genes: align by innovation id.
        var connections = new Dictionary<long, ConnectionGene>();
        foreach (ConnectionGene connection in b.Connections)
        {
            connections[connection.InnovationId] = connection;
        }

        foreach (ConnectionGene connection in a.Connections)
        {
            if (connections.TryGetValue(connection.InnovationId, out ConnectionGene? other))
            {
                // Matching gene: average the weights, enable if either parent enables it. From/To come
                // from a (identical to b's for a shared innovation id).
                connections[connection.InnovationId] = connection with
                {
                    Weight = (connection.Weight + other.Weight) / 2.0,
                    Enabled = connection.Enabled || other.Enabled,
                };
            }
            else
            {
                // Disjoint/excess in a: inherit as-is.
                connections[connection.InnovationId] = connection;
            }
        }

        return new NeatGenome
        {
            NetworkType = a.NetworkType,
            Nodes = nodes.Values.OrderBy(n => n.Id).ToList(),
            Connections = connections.Values.OrderBy(c => c.InnovationId).ToList(),
        };
    }
}
