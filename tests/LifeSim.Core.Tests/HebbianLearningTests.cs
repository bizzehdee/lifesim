using LifeSim.Core.Configuration;
using LifeSim.Core.Neat;

namespace LifeSim.Core.Tests;

public class HebbianLearningTests
{
    private static readonly LearningConfig Config = new() { LearnRate = 0.1, WeightClamp = 8.0 };

    private static NeatGenome BrainWith(double weight, double preState, double postState) => new()
    {
        Nodes = [new NodeGene { Id = 1, Type = NodeType.Input, State = preState }, new NodeGene { Id = 2, Type = NodeType.Output, State = postState }],
        Connections = [new ConnectionGene { InnovationId = 1, From = 1, To = 2, Weight = weight, Enabled = true }],
    };

    // A germline whose one connection has the given weight (aligned to BrainWith by innovation id).
    private static NeatGenome GermlineWith(double weight) => new()
    {
        Nodes = [new NodeGene { Id = 1, Type = NodeType.Input }, new NodeGene { Id = 2, Type = NodeType.Output }],
        Connections = [new ConnectionGene { InnovationId = 1, From = 1, To = 2, Weight = weight, Enabled = true }],
    };

    private static double WeightOf(NeatGenome brain) => brain.Connections[0].Weight;

    [Fact]
    public void Apply_nudgesWeightByPlasticityRewardAndCoactivation()
    {
        NeatGenome brain = BrainWith(1.0, 0.5, 2.0);
        NeatGenome learned = HebbianLearning.Apply(brain, GermlineWith(1.0), reward: 1.0, plasticity: 0.5, decay: 0.0, Config);

        // Δw = plasticity·rate·reward·pre·post = 0.5·0.1·1·0.5·2 = 0.05 (no decay: germline == weight)
        Assert.Equal(1.05, WeightOf(learned), precision: 10);
    }

    [Fact]
    public void Apply_negativeReward_pushesTheWeightDown()
    {
        NeatGenome learned = HebbianLearning.Apply(BrainWith(1.0, 0.5, 2.0), GermlineWith(1.0), reward: -1.0, plasticity: 0.5, decay: 0.0, Config);
        Assert.Equal(0.95, WeightOf(learned), precision: 10);
    }

    [Theory]
    [InlineData(0.0, 1.0)]  // no plasticity
    [InlineData(0.5, 0.0)]  // no reward
    public void Apply_isANoOp_withoutPlasticityOrReward(double plasticity, double reward)
    {
        NeatGenome brain = BrainWith(1.0, 0.5, 2.0);
        Assert.Same(brain, HebbianLearning.Apply(brain, GermlineWith(1.0), reward, plasticity, decay: 0.0, Config));
    }

    [Fact]
    public void Apply_clampsLearnedWeightToTheBound()
    {
        NeatGenome learned = HebbianLearning.Apply(BrainWith(7.5, 1.0, 1.0), GermlineWith(7.5), reward: 100.0, plasticity: 1.0, decay: 0.0, Config);
        Assert.Equal(Config.WeightClamp, WeightOf(learned), precision: 10);
    }

    [Fact]
    public void Apply_decay_relaxesLearnedWeightBackTowardTheGermline()
    {
        // No reward, so only decay acts: pull a drifted weight (2.0) toward its germline (1.0) by
        // decay·DecayScale = 1·0.1 of the gap → 2.0 + 0.1·(1.0−2.0) = 1.9.
        NeatGenome learned = HebbianLearning.Apply(BrainWith(2.0, 0.0, 0.0), GermlineWith(1.0), reward: 0.0, plasticity: 1.0, decay: 1.0, Config);
        Assert.Equal(1.9, WeightOf(learned), precision: 10);
    }
}
