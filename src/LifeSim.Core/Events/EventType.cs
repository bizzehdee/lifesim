namespace LifeSim.Core.Events;

/// <summary>The three global stochastic shocks the engine injects to punish over-specialization.</summary>
public enum EventType
{
    /// <summary>Halts ground ambient energy regeneration for a duration.</summary>
    ResourceBlight,

    /// <summary>Drains energy from organisms in crowded sub-regions for a duration.</summary>
    DensityPlague,

    /// <summary>Shifts effective temperature by ±<c>temperature_anomaly_magnitude</c> for a duration.</summary>
    ClimaticAnomaly,
}
