using LifeSim.Core.Configuration;

namespace LifeSim.Core.Organisms;

/// <summary>
/// The inheritable structural traits. Thermal Envelope is modelled as a
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

    /// <summary>
    /// Evolvable generosity: the fraction of its own energy the organism donates
    /// when it performs a Share action. Under selection this drifts freely — lineages can evolve
    /// toward hoarding (→ 0) or over-sharing (→ 1), whichever the local kin economy favours. The
    /// brain still decides <em>whether</em> to share and relatedness gates <em>whether it lands</em>
    /// (§20); this trait sets <em>how much</em>.
    /// </summary>
    public double ShareFraction { get; init; }

    /// <summary>
    /// Body size in cells: the body plan for aggregate multicellularity. 1 is
    /// unicellular (a plain organism); larger bodies pay volume upkeep (∝ N) but can only feed
    /// through their surface (∝ N^⅔), so the square-cube law caps the viable value. Ignored when
    /// multicellularity is disabled (every body is then a single cell).
    /// </summary>
    public double CellCount { get; init; } = 1.0;

    /// <summary>Specialisation weights: relative propensities toward each cell job, normalised to fractions at use.</summary>
    public double GermWeight { get; init; }
    public double FeederWeight { get; init; }
    public double StoreWeight { get; init; }
    public double DefenderWeight { get; init; }
    public double MoverWeight { get; init; }
    public double SensorWeight { get; init; }

    /// <summary>Clamps every trait to its hard min/max.</summary>
    public Genome Clamped(TraitBounds bounds) => this with
    {
        Size = Clamp(Size, bounds.Size),
        SpeedCapacity = Clamp(SpeedCapacity, bounds.SpeedCapacity),
        ThermalCenter = Clamp(ThermalCenter, bounds.ThermalCenter),
        ThermalWidth = Clamp(ThermalWidth, bounds.ThermalWidth),
        EnvRadius = Clamp(EnvRadius, bounds.EnvRadius),
        OrgRadius = Clamp(OrgRadius, bounds.OrgRadius),
        SensoryAcuity = Clamp(SensoryAcuity, bounds.SensoryAcuity),
        ShareFraction = Clamp(ShareFraction, bounds.ShareFraction),
        CellCount = Clamp(CellCount, bounds.CellCount),
        GermWeight = Clamp(GermWeight, bounds.GermWeight),
        FeederWeight = Clamp(FeederWeight, bounds.FeederWeight),
        StoreWeight = Clamp(StoreWeight, bounds.StoreWeight),
        DefenderWeight = Clamp(DefenderWeight, bounds.DefenderWeight),
        MoverWeight = Clamp(MoverWeight, bounds.MoverWeight),
        SensorWeight = Clamp(SensorWeight, bounds.SensorWeight),
    };

    /// <summary>
    /// Mid-range genome for genesis organisms: the midpoint of every bound, with two
    /// deliberate exceptions. <see cref="ShareFraction"/> is seeded separately from
    /// <c>CooperationConfig.ShareFraction</c> (§20). <see cref="CellCount"/> starts at 1 (unicellular,
    /// not the bound midpoint) so founders begin the multicellular transition from a single cell (§21);
    /// the equal specialisation-weight midpoints make that cell a generalist.
    /// </summary>
    public static Genome MidRange(TraitBounds bounds) => new()
    {
        Size = Midpoint(bounds.Size),
        SpeedCapacity = Midpoint(bounds.SpeedCapacity),
        ThermalCenter = Midpoint(bounds.ThermalCenter),
        ThermalWidth = Midpoint(bounds.ThermalWidth),
        EnvRadius = Midpoint(bounds.EnvRadius),
        OrgRadius = Midpoint(bounds.OrgRadius),
        SensoryAcuity = Midpoint(bounds.SensoryAcuity),
        ShareFraction = Midpoint(bounds.ShareFraction),
        CellCount = bounds.CellCount.Min,
        GermWeight = Midpoint(bounds.GermWeight),
        FeederWeight = Midpoint(bounds.FeederWeight),
        StoreWeight = Midpoint(bounds.StoreWeight),
        DefenderWeight = Midpoint(bounds.DefenderWeight),
        MoverWeight = Midpoint(bounds.MoverWeight),
        SensorWeight = Midpoint(bounds.SensorWeight),
    };

    private static double Clamp(double value, TraitBounds.Range range) =>
        Math.Clamp(value, range.Min, range.Max);

    private static double Midpoint(TraitBounds.Range range) => (range.Min + range.Max) / 2.0;
}
