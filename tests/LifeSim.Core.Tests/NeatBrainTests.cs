using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class NeatBrainTests
{
    private static readonly double[] Inputs = Enumerable.Repeat(1.0, NeatTopology.InputCount).ToArray();

    [Fact]
    public void Propagate_withSteps_chainsSingleStepsAndClampsToOne()
    {
        // A larger multicellular body runs several recurrent steps per tick (lifesim.md §4, §21):
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
        // One-tick propagation latency (lifesim.md §4): every node reads its inputs' *previous*
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
}
