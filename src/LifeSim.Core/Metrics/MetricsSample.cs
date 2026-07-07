using LifeSim.Core.Snapshot;

namespace LifeSim.Core.Metrics;

/// <summary>One tick's metrics tagged with its world time — the unit of a metrics stream.</summary>
public sealed record MetricsSample(long Tick, SimulationMetrics Metrics);
