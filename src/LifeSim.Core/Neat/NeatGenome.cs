namespace LifeSim.Core.Neat;

/// <summary>
/// An organism's brain: a recurrent network of node and connection genes.
/// Immutable — a tick's evaluation produces a new <see cref="NeatGenome"/> with updated node
/// <see cref="NodeGene.State"/> rather than mutating in place, mirroring the synchronous
/// commit-together update rule.
/// </summary>
public sealed record NeatGenome
{
    public IReadOnlyList<NodeGene> Nodes { get; init; } = [];
    public IReadOnlyList<ConnectionGene> Connections { get; init; } = [];
    public string NetworkType { get; init; } = "recurrent";

    // The compiler-generated record equality would compare Nodes/Connections by list reference
    // (List<T> doesn't override Equals), so two structurally-identical genomes deserialized into
    // separate list instances would compare unequal. Override with sequence equality instead.
    public bool Equals(NeatGenome? other) =>
        other is not null
        && NetworkType == other.NetworkType
        && Nodes.SequenceEqual(other.Nodes)
        && Connections.SequenceEqual(other.Connections);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(NetworkType);
        foreach (NodeGene node in Nodes)
        {
            hash.Add(node);
        }

        foreach (ConnectionGene connection in Connections)
        {
            hash.Add(connection);
        }

        return hash.ToHashCode();
    }
}
