namespace LifeSim.Core.Snapshot;

/// <summary>
/// Analytics as first-class output (lifesim.md §14). Phase 4 only needs population and the
/// extinction flag to satisfy the full-extinction exit criteria; the rest of §14's metrics
/// (births/deaths, energy stats, trait histograms, etc.) are added in Phase 10.
/// </summary>
public sealed record SimulationMetrics
{
    public long Population { get; init; }

    /// <summary>Set once population reaches zero; the engine halts and never auto-reseeds (lifesim.md §17).</summary>
    public bool Extinct { get; init; }
}
