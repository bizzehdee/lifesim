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

    private static double WeightOf(NeatGenome brain) => brain.Connections[0].Weight;

    [Fact]
    public void Apply_nudgesWeightByPlasticityRewardAndCoactivation()
    {
        NeatGenome learned = HebbianLearning.Apply(BrainWith(1.0, 0.5, 2.0), reward: 1.0, plasticity: 0.5, Config);

        // Δw = plasticity·rate·reward·pre·post = 0.5·0.1·1·0.5·2 = 0.05
        Assert.Equal(1.05, WeightOf(learned), precision: 10);
    }

    [Fact]
    public void Apply_negativeReward_pushesTheWeightDown()
    {
        NeatGenome learned = HebbianLearning.Apply(BrainWith(1.0, 0.5, 2.0), reward: -1.0, plasticity: 0.5, Config);
        Assert.Equal(0.95, WeightOf(learned), precision: 10);
    }

    [Theory]
    [InlineData(0.0, 1.0)]  // no plasticity
    [InlineData(0.5, 0.0)]  // no reward
    public void Apply_isANoOp_withoutPlasticityOrReward(double plasticity, double reward)
    {
        NeatGenome brain = BrainWith(1.0, 0.5, 2.0);
        Assert.Same(brain, HebbianLearning.Apply(brain, reward, plasticity, Config));
    }

    [Fact]
    public void Apply_clampsLearnedWeightToTheBound()
    {
        NeatGenome learned = HebbianLearning.Apply(BrainWith(7.5, 1.0, 1.0), reward: 100.0, plasticity: 1.0, Config);
        Assert.Equal(Config.WeightClamp, WeightOf(learned), precision: 10);
    }
}
