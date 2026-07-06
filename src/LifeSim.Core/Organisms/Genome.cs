using LifeSim.Core.Configuration;

namespace LifeSim.Core.Organisms;

/// <summary>
/// The inheritable structural traits (lifesim.md §3, §8). Thermal Envelope is modelled as a
/// center ± half-width band (<see cref="ThermalCenter"/>, <see cref="ThermalWidth"/>); every
/// trait is hard-bounded by the configured <see cref="TraitBounds"/>.
/// </summary>
public sealed record Genome
{
    public double Size { get; init; }
    public double SpeedCapacity { get; init; }
    public double ThermalCenter { get; init; }
    public double ThermalWidth { get; init; }
    public double EnvRadius { get; init; }
    public double OrgRadius { get; init; }
    public double SensoryAcuity { get; init; }

    /// <summary>Clamps every trait to its hard min/max (lifesim.md §3, §8).</summary>
    public Genome Clamped(TraitBounds bounds) => this with
    {
        Size = Clamp(Size, bounds.Size),
        SpeedCapacity = Clamp(SpeedCapacity, bounds.SpeedCapacity),
        ThermalCenter = Clamp(ThermalCenter, bounds.ThermalCenter),
        ThermalWidth = Clamp(ThermalWidth, bounds.ThermalWidth),
        EnvRadius = Clamp(EnvRadius, bounds.EnvRadius),
        OrgRadius = Clamp(OrgRadius, bounds.OrgRadius),
        SensoryAcuity = Clamp(SensoryAcuity, bounds.SensoryAcuity),
    };

    /// <summary>Mid-range genome for genesis organisms (lifesim.md §17): the midpoint of every bound.</summary>
    public static Genome MidRange(TraitBounds bounds) => new()
    {
        Size = Midpoint(bounds.Size),
        SpeedCapacity = Midpoint(bounds.SpeedCapacity),
        ThermalCenter = Midpoint(bounds.ThermalCenter),
        ThermalWidth = Midpoint(bounds.ThermalWidth),
        EnvRadius = Midpoint(bounds.EnvRadius),
        OrgRadius = Midpoint(bounds.OrgRadius),
        SensoryAcuity = Midpoint(bounds.SensoryAcuity),
    };

    private static double Clamp(double value, TraitBounds.Range range) =>
        Math.Clamp(value, range.Min, range.Max);

    private static double Midpoint(TraitBounds.Range range) => (range.Min + range.Max) / 2.0;
}
