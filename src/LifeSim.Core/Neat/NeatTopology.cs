namespace LifeSim.Core.Neat;

/// <summary>
/// The fixed input/output node layout shared by every genesis brain.
/// Input and output nodes are canonical/shared structure — not fresh mutations — so their
/// innovation ids are simple deterministic constants rather than draws from the mutable
/// <c>next_innovation_id</c> counter; that counter starts at <see cref="ReservedInnovationIdCount"/>
/// so Phase 8's structural mutations never collide with this baseline.
/// </summary>
public static class NeatTopology
{
    /// <summary>The fixed sensory vector width — see <see cref="LifeSim.Core.Sensing.SensoryField"/>.</summary>
    public const int InputCount = 26;

    /// <summary>The 15 action outputs — matches <see cref="Organisms.OrganismAction"/>.</summary>
    public const int OutputCount = 15;

    /// <summary>Total innovation ids reserved for the fixed genesis topology (nodes + full connectivity).</summary>
    public const long ReservedInnovationIdCount = InputCount + OutputCount + ((long)InputCount * OutputCount);

    public static readonly IReadOnlyList<long> InputNodeIds =
        Enumerable.Range(0, InputCount).Select(i => (long)i).ToList();

    public static readonly IReadOnlyList<long> OutputNodeIds =
        Enumerable.Range(InputCount, OutputCount).Select(i => (long)i).ToList();

    /// <summary>The shared innovation id for the canonical (inputIndex -> outputIndex) genesis connection.</summary>
    public static long ConnectionInnovationId(int inputIndex, int outputIndex) =>
        InputCount + OutputCount + ((long)inputIndex * OutputCount) + outputIndex;
}
