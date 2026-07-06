namespace LifeSim.Core.Snapshot;

/// <summary>
/// Monotonic counters that are part of deterministic state (lifesim.md §4, §9, §12): innovation
/// IDs for NEAT genes and organism IDs. Serialized in every snapshot so IDs are stable and never
/// reused across a resume.
/// </summary>
public sealed record EvolutionBookkeeping
{
    public long NextInnovationId { get; init; }
    public long NextOrganismId { get; init; }
}
