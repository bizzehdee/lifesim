namespace LifeSim.Core.Events;

/// <summary>
/// One active environmental event modifier, stored in the snapshot's
/// <c>environment_modifiers</c> block. Aged each Environment phase and removed once
/// <see cref="RemainingTicks"/> reaches zero. <see cref="Magnitude"/> carries the signed
/// temperature shift for a <see cref="EventType.ClimaticAnomaly"/> (±°C); it is 0 for blight and
/// plague, whose effects are governed entirely by config.
/// </summary>
public sealed record EnvironmentModifier
{
    public EventType Type { get; init; }
    public long StartTick { get; init; }
    public int RemainingTicks { get; init; }
    public double Magnitude { get; init; }
}
