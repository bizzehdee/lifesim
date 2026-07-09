using LifeSim.App.Presentation;
using LifeSim.Core.Neat;

namespace LifeSim.App.Tests;

public class BrainIntelligenceTests
{
    private static NodeGene Node(long id, NodeType type) => new() { Id = id, Type = type };

    private static ConnectionGene Conn(long innovation, long from, long to, bool enabled = true) =>
        new() { InnovationId = innovation, From = from, To = to, Weight = 1.0, Enabled = enabled };

    // A bare reflex net: one input wired straight to one output.
    private static NeatGenome Bare() => new()
    {
        Nodes = [Node(0, NodeType.Input), Node(1, NodeType.Output)],
        Connections = [Conn(10, 0, 1)],
    };

    [Fact]
    public void Score_isBoundedTo0To100_andEmptyBrainScoresZero()
    {
        Assert.Equal(0.0, BrainIntelligence.Score(new NeatGenome()));

        double bare = BrainIntelligence.Score(Bare());
        Assert.InRange(bare, 0.0, 100.0);
        Assert.True(bare > 0.0);
    }

    [Fact]
    public void Score_risesWithDepthAndHiddenUnits()
    {
        // input -> hidden -> output: deeper and one processing unit vs the bare reflex net.
        var deep = new NeatGenome
        {
            Nodes = [Node(0, NodeType.Input), Node(1, NodeType.Output), Node(2, NodeType.Hidden)],
            Connections = [Conn(10, 0, 2), Conn(11, 2, 1)],
        };

        Assert.True(BrainIntelligence.Score(deep) > BrainIntelligence.Score(Bare()));
    }

    [Fact]
    public void Score_countsFunctionalStructureOnly_soVestigialNodesDoNotInflateIt()
    {
        // Bare net plus a dangling hidden node fed from the input but with no path to any output — it
        // can't affect behaviour, so it must not raise the cognition score.
        var withVestigial = new NeatGenome
        {
            Nodes = [Node(0, NodeType.Input), Node(1, NodeType.Output), Node(2, NodeType.Hidden)],
            Connections = [Conn(10, 0, 1), Conn(11, 0, 2)], // node 2 never reaches the output
        };

        Assert.Equal(BrainIntelligence.Score(Bare()), BrainIntelligence.Score(withVestigial), precision: 9);
    }

    [Fact]
    public void Score_ignoresDisabledConnections()
    {
        // The hidden path exists but is disabled → it isn't functional, so this scores like the bare net.
        var disabledPath = new NeatGenome
        {
            Nodes = [Node(0, NodeType.Input), Node(1, NodeType.Output), Node(2, NodeType.Hidden)],
            Connections = [Conn(10, 0, 1), Conn(11, 0, 2, enabled: false), Conn(12, 2, 1, enabled: false)],
        };

        Assert.Equal(BrainIntelligence.Score(Bare()), BrainIntelligence.Score(disabledPath), precision: 9);
    }
}
