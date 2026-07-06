using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;

namespace LifeSim.Core.Tests;

public class NeatGenomeFactoryTests
{
    [Fact]
    public void CreateMinimalFullyConnected_hasNoHiddenNodes()
    {
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));

        Assert.Equal(NeatTopology.InputCount + NeatTopology.OutputCount, brain.Nodes.Count);
        Assert.DoesNotContain(brain.Nodes, n => n.Type == NodeType.Hidden);
        Assert.Equal(NeatTopology.InputCount, brain.Nodes.Count(n => n.Type == NodeType.Input));
        Assert.Equal(NeatTopology.OutputCount, brain.Nodes.Count(n => n.Type == NodeType.Output));
    }

    [Fact]
    public void CreateMinimalFullyConnected_wiresEveryInputToEveryOutput()
    {
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));

        Assert.Equal(NeatTopology.InputCount * NeatTopology.OutputCount, brain.Connections.Count);
        Assert.All(brain.Connections, c => Assert.True(c.Enabled));

        var pairs = brain.Connections.Select(c => (c.From, c.To)).ToHashSet();
        foreach (long inputId in NeatTopology.InputNodeIds)
        {
            foreach (long outputId in NeatTopology.OutputNodeIds)
            {
                Assert.Contains((inputId, outputId), pairs);
            }
        }
    }

    [Fact]
    public void CreateMinimalFullyConnected_connectionsHaveUniqueInnovationIds()
    {
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));
        Assert.Equal(brain.Connections.Count, brain.Connections.Select(c => c.InnovationId).Distinct().Count());
    }

    [Fact]
    public void CreateMinimalFullyConnected_isDeterministicForTheSamePrngState()
    {
        NeatGenome a = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(2024));
        NeatGenome b = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(2024));

        Assert.Equal(a, b);
    }

    [Fact]
    public void CreateMinimalFullyConnected_allNodeStateStartsAtZero()
    {
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));
        Assert.All(brain.Nodes, n => Assert.Equal(0.0, n.State));
    }

    [Fact]
    public void ReservedInnovationIdCount_isBeyondEveryGenesisId()
    {
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));

        Assert.All(brain.Nodes, n => Assert.True(n.Id < NeatTopology.ReservedInnovationIdCount));
        Assert.All(brain.Connections, c => Assert.True(c.InnovationId < NeatTopology.ReservedInnovationIdCount));
    }
}
