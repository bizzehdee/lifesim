using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;

namespace LifeSim.Core.Neat;

/// <summary>
/// Applies inheritable brain mutation to an offspring's NEAT genome in a fixed
/// order: weight perturbation, then new-connection mutation, then node-split mutation. Structural
/// mutations draw fresh innovation ids from the shared monotonic counter — advanced only here, in
/// the Birth Commit phase, in ascending offspring-id order. All stochastic
/// choices draw from the mutation PRNG stream. Recurrent (cycle-creating, including self-loop)
/// connections are permitted, so no acyclicity check is performed.
/// </summary>
public static class NeatMutator
{
    public static NeatGenome Mutate(
        NeatGenome genome, MutationConfig config, Prng mutationStream, InnovationIdAllocator innovationIds)
    {
        ArgumentNullException.ThrowIfNull(genome);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(mutationStream);
        ArgumentNullException.ThrowIfNull(innovationIds);

        NeatGenome mutated = MutateWeights(genome, config, mutationStream);
        mutated = MaybeAddConnection(mutated, config, mutationStream, innovationIds);
        mutated = MaybeAddNode(mutated, config, mutationStream, innovationIds);
        return mutated;
    }

    /// <summary>
    /// Perturbs each connection weight with probability <see cref="MutationConfig.WeightMutationRate"/>
    /// by a Gaussian step scaled by <see cref="MutationConfig.WeightMutationPower"/>.
    /// Draws happen in ascending innovation-id order so the sequence is independent of list storage
    /// order.
    /// </summary>
    private static NeatGenome MutateWeights(NeatGenome genome, MutationConfig config, Prng mutationStream)
    {
        var perturbed = new Dictionary<long, double>();
        foreach (ConnectionGene connection in genome.Connections.OrderBy(c => c.InnovationId))
        {
            if (mutationStream.NextDouble() < config.WeightMutationRate)
            {
                perturbed[connection.InnovationId] =
                    connection.Weight + (mutationStream.NextGaussian() * config.WeightMutationPower);
            }
        }

        if (perturbed.Count == 0)
        {
            return genome;
        }

        List<ConnectionGene> connections = genome.Connections
            .Select(c => perturbed.TryGetValue(c.InnovationId, out double weight) ? c with { Weight = weight } : c)
            .ToList();
        return genome with { Connections = connections };
    }

    /// <summary>
    /// With probability <see cref="MutationConfig.ConnectionMutationRate"/>, adds one connection
    /// between a currently-unconnected (from, to) pair. Input nodes are never a
    /// target (they carry raw sensory readings and have no incoming edges); every other node is a
    /// valid source or target, and self-loops/cycles are allowed (recurrent net). Candidate pairs
    /// are enumerated in ascending (from, to) order and one is chosen by the mutation stream.
    /// </summary>
    private static NeatGenome MaybeAddConnection(
        NeatGenome genome, MutationConfig config, Prng mutationStream, InnovationIdAllocator innovationIds)
    {
        if (mutationStream.NextDouble() >= config.ConnectionMutationRate)
        {
            return genome;
        }

        var existing = new HashSet<(long From, long To)>(genome.Connections.Select(c => (c.From, c.To)));
        List<long> sources = genome.Nodes.Select(n => n.Id).OrderBy(id => id).ToList();
        List<long> targets = genome.Nodes.Where(n => n.Type != NodeType.Input).Select(n => n.Id).OrderBy(id => id).ToList();

        var candidates = new List<(long From, long To)>();
        foreach (long from in sources)
        {
            foreach (long to in targets)
            {
                if (!existing.Contains((from, to)))
                {
                    candidates.Add((from, to));
                }
            }
        }

        if (candidates.Count == 0)
        {
            // Fully connected already — the roll is still consumed above, keeping draw counts stable.
            return genome;
        }

        (long From, long To) chosen = candidates[mutationStream.NextInt(candidates.Count)];
        double weight = (mutationStream.NextDouble() * 2.0) - 1.0;
        var connection = new ConnectionGene
        {
            InnovationId = innovationIds.Allocate(),
            From = chosen.From,
            To = chosen.To,
            Weight = weight,
            Enabled = true,
        };

        List<ConnectionGene> connections = genome.Connections.Append(connection).ToList();
        return genome with { Connections = connections, RuntimePlan = null };
    }

    /// <summary>
    /// With probability <see cref="MutationConfig.NodeMutationRate"/>, splits an enabled connection
    /// by inserting a new hidden node: the original connection is disabled, a
    /// <c>from → new</c> link (weight 1.0) and a <c>new → to</c> link (the original weight) replace
    /// it, so the freshly-inserted node is behaviorally near-neutral at birth. The new node id and
    /// both replacement innovation ids draw from the shared counter, in that fixed order.
    /// </summary>
    private static NeatGenome MaybeAddNode(
        NeatGenome genome, MutationConfig config, Prng mutationStream, InnovationIdAllocator innovationIds)
    {
        if (mutationStream.NextDouble() >= config.NodeMutationRate)
        {
            return genome;
        }

        List<ConnectionGene> enabled = genome.Connections.Where(c => c.Enabled).OrderBy(c => c.InnovationId).ToList();
        if (enabled.Count == 0)
        {
            return genome;
        }

        ConnectionGene split = enabled[mutationStream.NextInt(enabled.Count)];

        long newNodeId = innovationIds.Allocate();
        var newNode = new NodeGene { Id = newNodeId, Type = NodeType.Hidden };
        var inbound = new ConnectionGene
        {
            InnovationId = innovationIds.Allocate(),
            From = split.From,
            To = newNodeId,
            Weight = 1.0,
            Enabled = true,
        };
        var outbound = new ConnectionGene
        {
            InnovationId = innovationIds.Allocate(),
            From = newNodeId,
            To = split.To,
            Weight = split.Weight,
            Enabled = true,
        };

        List<NodeGene> nodes = genome.Nodes.Append(newNode).ToList();
        List<ConnectionGene> connections = genome.Connections
            .Select(c => c.InnovationId == split.InnovationId ? c with { Enabled = false } : c)
            .Append(inbound)
            .Append(outbound)
            .ToList();

        return genome with { Nodes = nodes, Connections = connections, RuntimePlan = null };
    }
}
