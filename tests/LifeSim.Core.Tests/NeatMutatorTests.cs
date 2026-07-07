using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;

namespace LifeSim.Core.Tests;

public class NeatMutatorTests
{
    private static InnovationIdAllocator NewAllocator() => new(NeatTopology.ReservedInnovationIdCount);

    private static NeatGenome Genesis() => NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(7));

    private static MutationConfig Off() => new MutationConfig() with
    {
        WeightMutationRate = 0.0,
        ConnectionMutationRate = 0.0,
        NodeMutationRate = 0.0,
    };

    [Fact]
    public void Mutate_withEverythingOff_returnsAnEqualGenome()
    {
        NeatGenome genesis = Genesis();

        NeatGenome mutated = NeatMutator.Mutate(genesis, Off(), new Prng(1), NewAllocator());

        Assert.Equal(genesis, mutated);
    }

    [Fact]
    public void Mutate_weightMutationOnly_perturbsWeightsWithoutTouchingStructure()
    {
        NeatGenome genesis = Genesis();
        MutationConfig config = Off() with { WeightMutationRate = 1.0, WeightMutationPower = 0.5 };

        NeatGenome mutated = NeatMutator.Mutate(genesis, config, new Prng(1), NewAllocator());

        Assert.Equal(genesis.Nodes.Count, mutated.Nodes.Count);
        Assert.Equal(genesis.Connections.Count, mutated.Connections.Count);
        Assert.NotEqual(genesis, mutated);

        // Same (from, to) topology, only weights differ.
        var before = genesis.Connections.Select(c => (c.InnovationId, c.From, c.To, c.Enabled)).ToHashSet();
        var after = mutated.Connections.Select(c => (c.InnovationId, c.From, c.To, c.Enabled)).ToHashSet();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Mutate_connectionMutationOnly_addsOneValidConnectionWithAFreshInnovationId()
    {
        NeatGenome genesis = Genesis();
        MutationConfig config = Off() with { ConnectionMutationRate = 1.0 };
        InnovationIdAllocator allocator = NewAllocator();
        long expectedInnovationId = allocator.NextId;

        NeatGenome mutated = NeatMutator.Mutate(genesis, config, new Prng(3), allocator);

        Assert.Equal(genesis.Nodes.Count, mutated.Nodes.Count);
        Assert.Equal(genesis.Connections.Count + 1, mutated.Connections.Count);

        ConnectionGene added = mutated.Connections.Single(c => c.InnovationId == expectedInnovationId);
        Assert.Equal(expectedInnovationId + 1, allocator.NextId);
        Assert.True(added.Enabled);

        // The target is never an input node, and the (from, to) pair was previously absent.
        var inputIds = genesis.Nodes.Where(n => n.Type == NodeType.Input).Select(n => n.Id).ToHashSet();
        Assert.DoesNotContain(added.To, inputIds);
        Assert.DoesNotContain((added.From, added.To), genesis.Connections.Select(c => (c.From, c.To)));
    }

    [Fact]
    public void Mutate_nodeMutationOnly_splitsAConnectionIntoAHiddenNode()
    {
        NeatGenome genesis = Genesis();
        MutationConfig config = Off() with { NodeMutationRate = 1.0 };
        InnovationIdAllocator allocator = NewAllocator();
        long newNodeId = allocator.NextId;

        NeatGenome mutated = NeatMutator.Mutate(genesis, config, new Prng(9), allocator);

        Assert.Equal(genesis.Nodes.Count + 1, mutated.Nodes.Count);
        Assert.Equal(genesis.Connections.Count + 2, mutated.Connections.Count);
        Assert.Equal(newNodeId + 3, allocator.NextId); // node id + two connection innovation ids

        NodeGene hidden = mutated.Nodes.Single(n => n.Id == newNodeId);
        Assert.Equal(NodeType.Hidden, hidden.Type);
        Assert.Equal(0.0, hidden.State);

        // Exactly one connection was newly disabled (the split one); its endpoints are re-bridged
        // through the new node with the canonical 1.0 / inherited-weight split.
        List<ConnectionGene> disabled = mutated.Connections.Where(c => !c.Enabled).ToList();
        ConnectionGene split = Assert.Single(disabled);

        ConnectionGene inbound = mutated.Connections.Single(c => c.To == newNodeId);
        ConnectionGene outbound = mutated.Connections.Single(c => c.From == newNodeId);
        Assert.Equal(split.From, inbound.From);
        Assert.Equal(1.0, inbound.Weight);
        Assert.Equal(split.To, outbound.To);
        Assert.Equal(split.Weight, outbound.Weight);
    }

    [Fact]
    public void Mutate_isDeterministicInBothGenomeAndInnovationCounter()
    {
        NeatGenome genesis = Genesis();
        MutationConfig config = new MutationConfig() with
        {
            WeightMutationRate = 1.0,
            ConnectionMutationRate = 1.0,
            NodeMutationRate = 1.0,
        };

        InnovationIdAllocator allocatorA = NewAllocator();
        InnovationIdAllocator allocatorB = NewAllocator();
        NeatGenome a = NeatMutator.Mutate(genesis, config, new Prng(555), allocatorA);
        NeatGenome b = NeatMutator.Mutate(genesis, config, new Prng(555), allocatorB);

        Assert.Equal(a, b);
        Assert.Equal(allocatorA.NextId, allocatorB.NextId);
    }

    [Fact]
    public void Mutate_permitsRecurrentConnections_includingSelfLoops()
    {
        // A tiny 1-input / 1-output net with one spare hidden node, so connection candidates
        // include self-loops and back-edges — cycle-creating links must be allowed, not rejected
        //.
        var genome = new NeatGenome
        {
            Nodes =
            [
                new NodeGene { Id = 0, Type = NodeType.Input },
                new NodeGene { Id = 1, Type = NodeType.Output },
                new NodeGene { Id = 2, Type = NodeType.Hidden },
            ],
            Connections = [new ConnectionGene { InnovationId = 100, From = 0, To = 1, Weight = 0.5, Enabled = true }],
        };

        MutationConfig config = Off() with { ConnectionMutationRate = 1.0 };
        InnovationIdAllocator allocator = NewAllocator();

        // Saturate: keep adding connections until no candidate pair remains.
        NeatGenome saturated = genome;
        for (int i = 0; i < 20; i++)
        {
            saturated = NeatMutator.Mutate(saturated, config, new Prng((ulong)(1000 + i)), allocator);
        }

        Assert.Contains(saturated.Connections, c => c.From == c.To);
        Assert.Contains(saturated.Connections, c => c.From == 1 && c.To == 2); // output -> hidden back-edge
    }
}
