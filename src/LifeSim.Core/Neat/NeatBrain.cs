using LifeSim.Core.Determinism;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Neat;

/// <summary>The result of one tick's brain evaluation: the genome with updated node state, and the selected action.</summary>
public sealed record NeatEvaluationResult(NeatGenome Genome, OrganismAction Action);

/// <summary>
/// The pure part of a tick's brain update (lifesim.md §4): the genome with advanced node state and
/// the softmax action distribution, computed with no randomness. Splitting this out lets the
/// expensive forward pass run in parallel across organisms while the single PRNG action roll stays
/// sequential in id order (lifesim.md §7, §9).
/// </summary>
public sealed record NeatPropagation(NeatGenome Genome, double[] Probabilities);

/// <summary>
/// Recurrent NEAT evaluation via a single synchronous update per tick (lifesim.md §4, §7): every
/// node computes its new activation purely from the *previous* tick's committed values, and all
/// nodes commit together. This makes node-processing order irrelevant to the result — there is no
/// "which node fires first" — which is what keeps a network with cycles fully deterministic.
/// </summary>
public static class NeatBrain
{
    /// <summary>
    /// Evaluates one tick. <paramref name="inputs"/> must be ordered to match
    /// <see cref="NeatTopology.InputNodeIds"/>. The output logits (one per
    /// <see cref="NeatTopology.OutputNodeIds"/>, in <see cref="OrganismAction"/> order) are the
    /// tanh-squashed output-node activations, softmaxed, then rolled against
    /// <paramref name="behaviorStream"/> (lifesim.md §4, §9).
    /// </summary>
    public static NeatEvaluationResult Evaluate(NeatGenome genome, IReadOnlyList<double> inputs, Prng behaviorStream)
    {
        ArgumentNullException.ThrowIfNull(behaviorStream);

        NeatPropagation propagation = Propagate(genome, inputs);
        OrganismAction action = SelectAction(propagation.Probabilities, behaviorStream);
        return new NeatEvaluationResult(propagation.Genome, action);
    }

    /// <summary>
    /// The randomness-free forward pass: advances node state and returns the softmax action
    /// distribution (lifesim.md §4). Pure and free of shared state, so it is safe to run for many
    /// organisms concurrently; the ensuing <see cref="SelectAction"/> roll must stay sequential.
    /// </summary>
    public static NeatPropagation Propagate(NeatGenome genome, IReadOnlyList<double> inputs)
    {
        ArgumentNullException.ThrowIfNull(genome);
        ArgumentNullException.ThrowIfNull(inputs);

        var previousState = new Dictionary<long, double>(genome.Nodes.Count);
        foreach (NodeGene node in genome.Nodes)
        {
            previousState[node.Id] = node.State;
        }

        // Fixed order per incoming edge set (lifesim.md §9): sum by ascending innovation id so
        // the weighted-sum reduction is not sensitive to the connection list's storage order.
        Dictionary<long, List<ConnectionGene>> incomingByTarget = genome.Connections
            .Where(c => c.Enabled)
            .GroupBy(c => c.To)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.InnovationId).ToList());

        var updatedNodes = new List<NodeGene>(genome.Nodes.Count);
        var newState = new Dictionary<long, double>(genome.Nodes.Count);

        foreach (NodeGene node in genome.Nodes)
        {
            double value;
            if (node.Type == NodeType.Input)
            {
                // Input nodes have no incoming edges — they simply carry this tick's fresh
                // sensory reading, which downstream nodes will read starting *next* tick.
                value = inputs[(int)node.Id];
            }
            else
            {
                double sum = 0.0;
                if (incomingByTarget.TryGetValue(node.Id, out List<ConnectionGene>? incoming))
                {
                    foreach (ConnectionGene connection in incoming)
                    {
                        sum += connection.Weight * previousState.GetValueOrDefault(connection.From, 0.0);
                    }
                }

                value = Math.Tanh(sum);
            }

            newState[node.Id] = value;
            updatedNodes.Add(node with { State = value });
        }

        var outputLogits = new double[NeatTopology.OutputCount];
        for (int i = 0; i < NeatTopology.OutputCount; i++)
        {
            outputLogits[i] = newState[NeatTopology.OutputNodeIds[i]];
        }

        double[] probabilities = Softmax(outputLogits);
        var updatedGenome = genome with { Nodes = updatedNodes };
        return new NeatPropagation(updatedGenome, probabilities);
    }

    /// <summary>
    /// The action-probability distribution implied by the brain's *current* committed output-node
    /// states — the same softmax the next <see cref="Evaluate"/> would roll against, computed without
    /// advancing or mutating anything. Pure read for inspection/rendering (lifesim.md §18); indices
    /// match <see cref="OrganismAction"/> / <see cref="NeatTopology.OutputNodeIds"/> order.
    /// </summary>
    public static double[] ActionProbabilities(NeatGenome genome)
    {
        ArgumentNullException.ThrowIfNull(genome);

        var stateById = new Dictionary<long, double>(genome.Nodes.Count);
        foreach (NodeGene node in genome.Nodes)
        {
            stateById[node.Id] = node.State;
        }

        var logits = new double[NeatTopology.OutputCount];
        for (int i = 0; i < NeatTopology.OutputCount; i++)
        {
            logits[i] = stateById.GetValueOrDefault(NeatTopology.OutputNodeIds[i], 0.0);
        }

        return Softmax(logits);
    }

    private static double[] Softmax(double[] logits)
    {
        double max = logits[0];
        for (int i = 1; i < logits.Length; i++)
        {
            max = Math.Max(max, logits[i]);
        }

        var exp = new double[logits.Length];
        double sum = 0.0;
        for (int i = 0; i < logits.Length; i++)
        {
            exp[i] = Math.Exp(logits[i] - max);
            sum += exp[i];
        }

        for (int i = 0; i < exp.Length; i++)
        {
            exp[i] /= sum;
        }

        return exp;
    }

    /// <summary>Rolls the behaviour stream once to pick an action from a softmax distribution (lifesim.md §4, §9).</summary>
    public static OrganismAction SelectAction(double[] probabilities, Prng behaviorStream)
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        ArgumentNullException.ThrowIfNull(behaviorStream);
        return WeightedSelect(probabilities, behaviorStream);
    }

    private static OrganismAction WeightedSelect(double[] probabilities, Prng behaviorStream)
    {
        double roll = behaviorStream.NextDouble();
        double cumulative = 0.0;
        for (int i = 0; i < probabilities.Length; i++)
        {
            cumulative += probabilities[i];
            if (roll < cumulative)
            {
                return (OrganismAction)i;
            }
        }

        // Floating-point rounding may leave the cumulative sum a hair under 1.0; fall back to the
        // last action rather than throwing.
        return (OrganismAction)(probabilities.Length - 1);
    }
}
