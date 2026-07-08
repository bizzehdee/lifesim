using LifeSim.Core.Configuration;

namespace LifeSim.Core.Neat;

/// <summary>
/// Within-life, reward-modulated Hebbian plasticity, with an evolvable stability–plasticity trade-off.
/// Each tick a plastic brain's enabled connections move by
/// <c>Δw = plasticity · learnRate · reward · pre · post  +  (decay · decayScale) · (germline − w)</c>:
/// the first term learns from the correlations active when the reward arrived, the second forgets by
/// relaxing back toward the inherited germline weight. Evolving both <c>plasticity</c> (how fast to
/// learn) and <c>learning_decay</c> (how fast to forget) puts the shape of the rule under selection, not
/// just its rate. A pure function — no PRNG, no shared state — so it is deterministic and safe to run
/// per-organism in parallel. Only the live brain changes; the germline is never touched, so learned
/// changes are not inherited.
/// </summary>
public static class HebbianLearning
{
    public static NeatGenome Apply(NeatGenome brain, NeatGenome germline, double reward, double plasticity, double decay, LearningConfig config)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(germline);
        ArgumentNullException.ThrowIfNull(config);

        if (plasticity <= 0.0)
        {
            return brain; // a fixed brain never diverged from its germline — nothing to learn or forget
        }

        double factor = plasticity * config.LearnRate * reward;
        double decayRate = Math.Clamp(decay, 0.0, 1.0) * config.DecayScale;
        if (factor == 0.0 && decayRate == 0.0)
        {
            return brain; // no reward this tick and no forgetting — a no-op
        }

        var state = new Dictionary<long, double>(brain.Nodes.Count);
        foreach (NodeGene node in brain.Nodes)
        {
            state[node.Id] = node.State;
        }

        var germlineWeight = new Dictionary<long, double>(germline.Connections.Count);
        foreach (ConnectionGene g in germline.Connections)
        {
            germlineWeight[g.InnovationId] = g.Weight;
        }

        var connections = new List<ConnectionGene>(brain.Connections.Count);
        foreach (ConnectionGene c in brain.Connections)
        {
            if (!c.Enabled)
            {
                connections.Add(c);
                continue;
            }

            double hebb = factor * state.GetValueOrDefault(c.From) * state.GetValueOrDefault(c.To);
            double toward = decayRate * (germlineWeight.GetValueOrDefault(c.InnovationId, c.Weight) - c.Weight);
            double weight = Math.Clamp(c.Weight + hebb + toward, -config.WeightClamp, config.WeightClamp);
            connections.Add(c with { Weight = weight });
        }

        return brain with { Connections = connections };
    }
}
