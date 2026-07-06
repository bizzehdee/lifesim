namespace LifeSim.Core.Neat;

/// <summary>A NEAT connection gene (lifesim.md §4, §12), keyed by a globally unique innovation id.</summary>
public sealed record ConnectionGene
{
    public long InnovationId { get; init; }
    public long From { get; init; }
    public long To { get; init; }
    public double Weight { get; init; }
    public bool Enabled { get; init; } = true;
}
