using LifeSim.Core.Configuration;

namespace LifeSim.Core.Neat;

/// <summary>
/// Within-life, reward-modulated Hebbian plasticity: nudges a live brain's connection weights toward
/// the correlations that were active when a reward arrived. For each enabled connection,
/// <c>Δw = plasticity · learnRate · reward · pre · post</c> (pre/post are the source/target node
/// activations from this tick's forward pass), clamped to a hard bound. A pure function — no PRNG, no
/// shared state — so it is deterministic and safe to run per-organism in parallel. Applies only to the
/// <em>live</em> brain; the germline is never touched, so learned changes are not inherited.
/// </summary>
public static class HebbianLearning
{
    public static NeatGenome Apply(NeatGenome brain, double reward, double plasticity, LearningConfig config)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(config);

        double factor = plasticity * config.LearnRate * reward;
        if (factor == 0.0)
        {
            return brain; // no plasticity or no reward this tick — nothing to learn
        }

        var state = new Dictionary<long, double>(brain.Nodes.Count);
        foreach (NodeGene node in brain.Nodes)
        {
            state[node.Id] = node.State;
        }

        var connections = new List<ConnectionGene>(brain.Connections.Count);
        foreach (ConnectionGene c in brain.Connections)
        {
            if (!c.Enabled)
            {
                connections.Add(c);
                continue;
            }

            double delta = factor * state.GetValueOrDefault(c.From) * state.GetValueOrDefault(c.To);
            double weight = Math.Clamp(c.Weight + delta, -config.WeightClamp, config.WeightClamp);
            connections.Add(c with { Weight = weight });
        }

        return brain with { Connections = connections };
    }
}
