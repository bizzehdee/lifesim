using LifeSim.Core.Determinism;
using LifeSim.Core.Configuration;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class NeatBrainTests
{
    private static readonly double[] Inputs = Enumerable.Repeat(1.0, NeatTopology.InputCount).ToArray();

    [Fact]
    public void Propagate_withSteps_chainsSingleStepsAndClampsToOne()
    {
        // A larger multicellular body runs several recurrent steps per tick:
        // N steps must equal chaining the single-step forward pass N times, and steps <= 1 is the base.
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(7));

        Assert.Equal(NeatBrain.Propagate(brain, Inputs).Genome.Nodes, NeatBrain.Propagate(brain, Inputs, 1).Genome.Nodes);
        Assert.Equal(NeatBrain.Propagate(brain, Inputs).Genome.Nodes, NeatBrain.Propagate(brain, Inputs, 0).Genome.Nodes);

        NeatPropagation manual = NeatBrain.Propagate(
            NeatBrain.Propagate(NeatBrain.Propagate(brain, Inputs).Genome, Inputs).Genome, Inputs);
        Assert.Equal(manual.Genome.Nodes, NeatBrain.Propagate(brain, Inputs, 3).Genome.Nodes);
    }

    [Fact]
    public void Evaluate_firstTick_outputsIgnoreCurrentInputs_sinceAllStateStartsAtZero()
    {
        // One-tick propagation latency: every node reads its inputs' *previous*
        // tick state, which is all-zero at birth, so the very first evaluation's outputs must be
        // exactly tanh(0) = 0 regardless of what's fed in this tick.
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));
        NeatEvaluationResult result = NeatBrain.Evaluate(brain, Inputs, new Prng(99));

        foreach (long outputId in NeatTopology.OutputNodeIds)
        {
            Assert.Equal(0.0, result.Genome.Nodes.Single(n => n.Id == outputId).State);
        }
    }

    [Fact]
    public void Evaluate_firstTick_commitsInputsAsThisTicksNodeState()
    {
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));
        NeatEvaluationResult result = NeatBrain.Evaluate(brain, Inputs, new Prng(99));

        for (int i = 0; i < NeatTopology.InputCount; i++)
        {
            long inputId = NeatTopology.InputNodeIds[i];
            Assert.Equal(Inputs[i], result.Genome.Nodes.Single(n => n.Id == inputId).State);
        }
    }

    [Fact]
    public void Evaluate_secondTick_outputsReflectFirstTicksInputs()
    {
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));
        NeatEvaluationResult first = NeatBrain.Evaluate(brain, Inputs, new Prng(99));
        NeatEvaluationResult second = NeatBrain.Evaluate(first.Genome, Inputs, new Prng(99));

        long outputId = NeatTopology.OutputNodeIds[0];
        double expectedSum = brain.Connections
            .Where(c => c.To == outputId)
            .Sum(c => c.Weight * Inputs[(int)c.From]);

        Assert.Equal(Math.Tanh(expectedSum), second.Genome.Nodes.Single(n => n.Id == outputId).State, precision: 10);
    }

    [Fact]
    public void Evaluate_isDeterministic_forIdenticalGenomeInputsAndPrngState()
    {
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));

        NeatEvaluationResult a = NeatBrain.Evaluate(brain, Inputs, new Prng(555));
        NeatEvaluationResult b = NeatBrain.Evaluate(brain, Inputs, new Prng(555));

        Assert.Equal(a.Genome, b.Genome);
        Assert.Equal(a.Action, b.Action);
    }

    [Fact]
    public void CompiledPropagation_matchesReferenceForRecurrentNetworkAndScrambledEdgeStorage()
    {
        NeatGenome baseline = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(77));
        long hiddenId = NeatTopology.ReservedInnovationIdCount + 100;
        long outputId = NeatTopology.OutputNodeIds[3];
        NeatGenome recurrent = baseline with
        {
            Nodes = baseline.Nodes.Append(new NodeGene { Id = hiddenId, Type = NodeType.Hidden, State = 0.25 }).ToList(),
            Connections = baseline.Connections
                .Append(new ConnectionGene { InnovationId = hiddenId + 1, From = 0, To = hiddenId, Weight = 0.7 })
                .Append(new ConnectionGene { InnovationId = hiddenId + 2, From = hiddenId, To = hiddenId, Weight = -0.4 })
                .Append(new ConnectionGene { InnovationId = hiddenId + 3, From = hiddenId, To = outputId, Weight = 1.2 })
                .OrderByDescending(c => c.InnovationId)
                .ToList(),
        };
        double[] inputs = Enumerable.Range(0, NeatTopology.InputCount).Select(i => Math.Sin(i)).ToArray();

        NeatGenome expected = ReferencePropagate(recurrent, inputs, steps: 4);
        NeatPropagation actual = NeatBrain.Propagate(recurrent, inputs, steps: 4);

        Assert.Equal(expected.Nodes, actual.Genome.Nodes);
        Assert.Equal(ReferenceProbabilities(expected), actual.Probabilities);
    }

    [Fact]
    public void StructuralMutation_recompilesAnAlreadyUsedRuntimePlan()
    {
        NeatGenome used = NeatBrain.Propagate(
            NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(91)), Inputs).Genome;
        var mutation = new MutationConfig
        {
            WeightMutationRate = 0.0,
            ConnectionMutationRate = 1.0,
            NodeMutationRate = 0.0,
        };
        NeatGenome structurallyChanged = NeatMutator.Mutate(
            used,
            mutation,
            new Prng(123),
            new InnovationIdAllocator(NeatTopology.ReservedInnovationIdCount + 500));

        NeatGenome expected = ReferencePropagate(structurallyChanged, Inputs, 2);
        NeatPropagation actual = NeatBrain.Propagate(structurallyChanged, Inputs, 2);

        Assert.True(structurallyChanged.Connections.Count > used.Connections.Count);
        Assert.Equal(expected.Nodes, actual.Genome.Nodes);
    }

    [Fact]
    public void Explain_capturesExactDistributionInputsAndChosenOutputDrivers()
    {
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(7));
        NeatPropagation primed = NeatBrain.Propagate(brain, Inputs);
        NeatPropagation decision = NeatBrain.Propagate(primed.Genome, Inputs);

        DecisionTrace trace = NeatBrain.Explain(
            decision, Inputs, OrganismAction.MoveNorth, tick: 2, maxSignals: 5);

        Assert.Equal(2, trace.Tick);
        Assert.Equal(OrganismAction.MoveNorth, trace.ChosenAction);
        Assert.Equal(decision.Probabilities, trace.ActionProbabilities);
        Assert.Equal(5, trace.StrongestInputs.Count);
        Assert.Equal(5, trace.StrongestContributions.Count);
        Assert.All(trace.StrongestContributions, contribution =>
            Assert.Equal(contribution.SourceActivation * contribution.Weight, contribution.WeightedSignal));
    }

    [Fact]
    public void Evaluate_biasedLogits_selectFavoredActionAtRoughlyTheSoftmaxRate()
    {
        // Hand-built genome: one input feeds only the Reproduce output, so after priming with one
        // tick that output's logit is tanh(weight) ~= 1 while every other output stays at 0 —
        // giving a known softmax distribution (favored ~= e/(e+10) ~= 21.4%, others ~= 7.86% each)
        // to check the weighted-PRNG selection against, rather than raw uniform odds (1/11 ~= 9%).
        long inputId = NeatTopology.InputNodeIds[0];
        long favoredOutputId = NeatTopology.OutputNodeIds[(int)OrganismAction.Reproduce];

        var nodes = new List<NodeGene>();
        foreach (long id in NeatTopology.InputNodeIds)
        {
            nodes.Add(new NodeGene { Id = id, Type = NodeType.Input });
        }

        foreach (long id in NeatTopology.OutputNodeIds)
        {
            nodes.Add(new NodeGene { Id = id, Type = NodeType.Output });
        }

        var genome = new NeatGenome
        {
            Nodes = nodes,
            Connections = [new ConnectionGene { InnovationId = 0, From = inputId, To = favoredOutputId, Weight = 20.0, Enabled = true }],
        };

        double[] inputs = new double[NeatTopology.InputCount];
        inputs[0] = 1.0;

        // Prime once so the favored output's incoming input becomes "previous tick" state.
        NeatEvaluationResult primed = NeatBrain.Evaluate(genome, inputs, new Prng(1));

        const int trials = 3000;
        var counts = new Dictionary<OrganismAction, int>();
        for (ulong seed = 0; seed < trials; seed++)
        {
            NeatEvaluationResult result = NeatBrain.Evaluate(primed.Genome, inputs, new Prng(seed));
            counts[result.Action] = counts.GetValueOrDefault(result.Action) + 1;
        }

        double favoredRate = counts.GetValueOrDefault(OrganismAction.Reproduce) / (double)trials;
        Assert.InRange(favoredRate, 0.15, 0.28);
        Assert.True(
            counts.GetValueOrDefault(OrganismAction.Reproduce) > counts.GetValueOrDefault(OrganismAction.Idle),
            "Expected the favored action to be picked more often than an unfavored one.");
    }

    private static NeatGenome ReferencePropagate(NeatGenome genome, double[] inputs, int steps)
    {
        for (int step = 0; step < steps; step++)
        {
            Dictionary<long, double> previous = genome.Nodes.ToDictionary(n => n.Id, n => n.State);
            Dictionary<long, List<ConnectionGene>> incoming = genome.Connections
                .Where(c => c.Enabled)
                .GroupBy(c => c.To)
                .ToDictionary(group => group.Key, group => group.OrderBy(c => c.InnovationId).ToList());
            var nodes = new List<NodeGene>(genome.Nodes.Count);
            foreach (NodeGene node in genome.Nodes)
            {
                double value = node.Type == NodeType.Input
                    ? inputs[(int)node.Id]
                    : Math.Tanh(incoming.GetValueOrDefault(node.Id, []).Sum(c => c.Weight * previous.GetValueOrDefault(c.From)));
                nodes.Add(node with { State = value });
            }

            genome = genome with { Nodes = nodes };
        }

        return genome;
    }

    private static double[] ReferenceProbabilities(NeatGenome genome)
    {
        Dictionary<long, double> state = genome.Nodes.ToDictionary(n => n.Id, n => n.State);
        double[] logits = NeatTopology.OutputNodeIds.Select(id => state[id]).ToArray();
        double max = logits.Max();
        double[] probabilities = logits.Select(value => Math.Exp(value - max)).ToArray();
        double sum = probabilities.Sum();
        return probabilities.Select(value => value / sum).ToArray();
    }
}
