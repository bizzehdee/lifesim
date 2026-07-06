namespace LifeSim.Core.Neat;

/// <summary>
/// The fixed input/output node layout shared by every genesis brain (lifesim.md §4, §17).
/// Input and output nodes are canonical/shared structure — not fresh mutations — so their
/// innovation ids are simple deterministic constants rather than draws from the mutable
/// <c>next_innovation_id</c> counter; that counter starts at <see cref="ReservedInnovationIdCount"/>
/// so Phase 8's structural mutations never collide with this baseline.
///
/// <para><b>Input count is a Phase 5 placeholder.</b> The real fixed sensory vector (lifesim.md
/// §13) is built in Phase 6 and may need a different width; when it does, this constant and the
/// genesis wiring below change together (there is no persisted production data yet to migrate).</para>
/// </summary>
public static class NeatTopology
{
    /// <summary>Placeholder input width (energy, age, tile temperature, biome friction) until Phase 6.</summary>
    public const int InputCount = 4;

    /// <summary>The 11 action outputs (lifesim.md §4) — matches <see cref="Organisms.OrganismAction"/>.</summary>
    public const int OutputCount = 11;

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
