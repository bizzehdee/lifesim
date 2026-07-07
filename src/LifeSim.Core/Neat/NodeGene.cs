namespace LifeSim.Core.Neat;

/// <summary>
/// A NEAT node gene. <see cref="Id"/> is a globally unique innovation
/// number, not a per-brain-local index. <see cref="State"/> is the node's persistent activation —
/// zero at birth, carried across ticks, and required to round-trip through serialization.
/// </summary>
public sealed record NodeGene
{
    public long Id { get; init; }
    public NodeType Type { get; init; }
    public string Activation { get; init; } = "tanh";
    public double State { get; init; }
}
