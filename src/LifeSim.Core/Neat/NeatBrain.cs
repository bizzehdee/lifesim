using LifeSim.Core.Determinism;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Neat;

/// <summary>The result of one tick's brain evaluation: the genome with updated node state, and the selected action.</summary>
public sealed record NeatEvaluationResult(NeatGenome Genome, OrganismAction Action);

/// <summary>
/// The pure part of a tick's brain update: the genome with advanced node state and
/// the softmax action distribution, computed with no randomness. Splitting this out lets the
/// expensive forward pass run in parallel across organisms while the single PRNG action roll stays
/// sequential in id order.
/// </summary>
public sealed record NeatPropagation(NeatGenome Genome, double[] Probabilities)
{
    internal NeatDecisionContext? DecisionContext { get; init; }
}

internal sealed record NeatDecisionContext(NeatExecutionPlan Plan, double[] StateBeforeFinalStep);

/// <summary>
/// Recurrent NEAT evaluation via a single synchronous update per tick: every
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
    /// <paramref name="behaviorStream"/>.
    /// </summary>
    public static NeatEvaluationResult Evaluate(NeatGenome genome, IReadOnlyList<double> inputs, Prng behaviorStream)
    {
        ArgumentNullException.ThrowIfNull(behaviorStream);

        NeatPropagation propagation = Propagate(genome, inputs);
        OrganismAction action = SelectAction(propagation.Probabilities, behaviorStream);
        return new NeatEvaluationResult(propagation.Genome, action);
    }

    /// <summary>
    /// Runs the forward pass <paramref name="steps"/> times over the same held inputs (/// §21): a larger multicellular body propagates its recurrent network more deeply per tick before
    /// acting. <paramref name="steps"/> is clamped to ≥ 1, so a single step is the base model. Pure.
    /// </summary>
    public static NeatPropagation Propagate(NeatGenome genome, IReadOnlyList<double> inputs, int steps)
    {
        return PropagateCompiled(genome, inputs, Math.Max(1, steps));
    }

    /// <summary>
    /// The randomness-free forward pass: advances node state and returns the softmax action
    /// distribution. Pure and free of shared state, so it is safe to run for many
    /// organisms concurrently; the ensuing <see cref="SelectAction"/> roll must stay sequential.
    /// </summary>
    public static NeatPropagation Propagate(NeatGenome genome, IReadOnlyList<double> inputs)
    {
        return PropagateCompiled(genome, inputs, 1);
    }

    private static NeatPropagation PropagateCompiled(
        NeatGenome genome,
        IReadOnlyList<double> inputs,
        int steps)
    {
        ArgumentNullException.ThrowIfNull(genome);
        ArgumentNullException.ThrowIfNull(inputs);

        NeatExecutionPlan plan = genome.RuntimePlan ?? NeatExecutionPlan.Compile(genome);
        var previousState = new double[genome.Nodes.Count];
        var nextState = new double[genome.Nodes.Count];
        for (int i = 0; i < genome.Nodes.Count; i++)
        {
            previousState[i] = genome.Nodes[i].State;
        }

        for (int step = 0; step < steps; step++)
        {
            for (int nodeIndex = 0; nodeIndex < plan.NodeTypes.Length; nodeIndex++)
            {
                if (plan.NodeTypes[nodeIndex] == NodeType.Input)
                {
                    // Fresh inputs become committed state; synchronous downstream reads use the
                    // previous recurrent step, preserving the one-step latency.
                    nextState[nodeIndex] = inputs[plan.InputOrdinals[nodeIndex]];
                    continue;
                }

                double sum = 0.0;
                foreach (NeatIncomingEdge edge in plan.Incoming[nodeIndex])
                {
                    if (edge.SourceNodeIndex >= 0)
                    {
                        sum += genome.Connections[edge.ConnectionIndex].Weight * previousState[edge.SourceNodeIndex];
                    }
                }

                nextState[nodeIndex] = Math.Tanh(sum);
            }

            (previousState, nextState) = (nextState, previousState);
        }

        var outputLogits = new double[NeatTopology.OutputCount];
        for (int i = 0; i < NeatTopology.OutputCount; i++)
        {
            int nodeIndex = plan.OutputNodeIndices[i];
            if (nodeIndex < 0)
            {
                throw new KeyNotFoundException($"Output node {NeatTopology.OutputNodeIds[i]} is missing.");
            }

            outputLogits[i] = previousState[nodeIndex];
        }

        double[] probabilities = Softmax(outputLogits);
        var updatedNodes = new List<NodeGene>(genome.Nodes.Count);
        for (int i = 0; i < genome.Nodes.Count; i++)
        {
            updatedNodes.Add(genome.Nodes[i] with { State = previousState[i] });
        }

        var updatedGenome = genome with { Nodes = updatedNodes, RuntimePlan = plan };
        return new NeatPropagation(updatedGenome, probabilities)
        {
            // After the final swap, nextState is the untouched state array that fed the final step.
            DecisionContext = new NeatDecisionContext(plan, nextState),
        };
    }

    /// <summary>Explains a selected action using the exact probabilities and final-step activations that produced it.</summary>
    public static DecisionTrace Explain(
        NeatPropagation propagation,
        IReadOnlyList<double> inputs,
        OrganismAction selectedAction,
        long tick,
        int maxSignals = 5)
    {
        ArgumentNullException.ThrowIfNull(propagation);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSignals);
        NeatDecisionContext context = propagation.DecisionContext
            ?? throw new ArgumentException("Propagation does not carry decision context.", nameof(propagation));
        int outputIndex = context.Plan.OutputNodeIndices[(int)selectedAction];

        List<DecisionContribution> contributions = context.Plan.Incoming[outputIndex]
            .Where(edge => edge.SourceNodeIndex >= 0)
            .Select(edge =>
            {
                double activation = context.StateBeforeFinalStep[edge.SourceNodeIndex];
                double weight = propagation.Genome.Connections[edge.ConnectionIndex].Weight;
                return new DecisionContribution
                {
                    SourceNodeId = context.Plan.NodeIds[edge.SourceNodeIndex],
                    SourceNodeType = context.Plan.NodeTypes[edge.SourceNodeIndex],
                    SourceActivation = activation,
                    Weight = weight,
                    WeightedSignal = activation * weight,
                };
            })
            .OrderByDescending(contribution => Math.Abs(contribution.WeightedSignal))
            .ThenBy(contribution => contribution.SourceNodeId)
            .Take(maxSignals)
            .ToList();

        List<DecisionInputSignal> strongestInputs = inputs
            .Select((value, index) => new DecisionInputSignal(index, value))
            .OrderByDescending(signal => Math.Abs(signal.Value))
            .ThenBy(signal => signal.InputIndex)
            .Take(maxSignals)
            .ToList();

        return new DecisionTrace
        {
            Tick = tick,
            ChosenAction = selectedAction,
            ActionProbabilities = propagation.Probabilities.ToList(),
            StrongestInputs = strongestInputs,
            StrongestContributions = contributions,
        };
    }

    /// <summary>
    /// The action-probability distribution implied by the brain's *current* committed output-node
    /// states — the same softmax the next <see cref="Evaluate"/> would roll against, computed without
    /// advancing or mutating anything. Pure read for inspection/rendering; indices
    /// match <see cref="OrganismAction"/> / <see cref="NeatTopology.OutputNodeIds"/> order.
    /// </summary>
    public static double[] ActionProbabilities(NeatGenome genome)
    {
        ArgumentNullException.ThrowIfNull(genome);

        NeatExecutionPlan plan = genome.RuntimePlan ?? NeatExecutionPlan.Compile(genome);

        var logits = new double[NeatTopology.OutputCount];
        for (int i = 0; i < NeatTopology.OutputCount; i++)
        {
            int nodeIndex = plan.OutputNodeIndices[i];
            logits[i] = nodeIndex >= 0 ? genome.Nodes[nodeIndex].State : 0.0;
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

    /// <summary>Rolls the behaviour stream once to pick an action from a softmax distribution.</summary>
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
