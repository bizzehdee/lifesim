using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;

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
    /// Evolvable metabolic frugality in [0, 1]: 0 is the baseline metabolism, 1 is maximally frugal.
    /// It scales down the organism's self-generated running costs (base upkeep, sensory tax, locomotion)
    /// toward — but never to — zero (a configurable floor, the thermodynamic waste-heat limit). The
    /// trade-off follows the biological rate–yield law: a frugal metabolism extracts less usable energy
    /// per graze (<see cref="Metabolism.EfficiencyYieldMultiplier"/>), so it pays off where food is
    /// scarce (ice/desert) but loses to fast full-yield generalists in rich biomes. Founders start at 0
    /// and lineages must evolve frugality up.
    /// </summary>
    public double MetabolicEfficiency { get; init; }

    /// <summary>
    /// Evolvable armour in [0, 1]: passive toughness that adds <em>defensive</em> combat mass, so a
    /// predator's kill chance drops — but, unlike simply being big, armour never boosts this organism's
    /// own attacks. Founders start at 0 and must evolve it. Costs metabolic upkeep (see
    /// <see cref="Metabolism.DefenseTax"/>).
    /// </summary>
    public double Armour { get; init; }

    /// <summary>
    /// Evolvable evasion in [0, 1]: an agility dodge that multiplicatively lowers a predator's kill
    /// chance (independent of mass), up to a configured cap. Founders start at 0. Costs metabolic upkeep.
    /// </summary>
    public double Evasion { get; init; }

    /// <summary>
    /// Evolvable toxicity in [0, 1]: on contact, a predator that attacks this organism takes toxin
    /// damage proportional to it — win or lose — so toxic prey are costly to eat (aposematism emerges by
    /// selection, since attackers can't sense toxicity directly). Founders start at 0. Costs upkeep.
    /// </summary>
    public double Toxicity { get; init; }

    /// <summary>
    /// Evolvable neural plasticity in [0, 1]: the learning rate for within-life, reward-modulated
    /// Hebbian weight change. 0 is a fixed brain (learning is evolved in, so a no-learning baseline is
    /// always available); higher values let the live brain adapt faster during life. Not inherited as
    /// learned weights — offspring inherit the germline (Darwinian; see <see cref="Organism.Germline"/>)
    /// — but the <em>capacity</em> to learn is heritable, so the Baldwin effect can operate. Costs
    /// metabolic upkeep (see <see cref="Metabolism.PlasticityTax"/>).
    /// </summary>
    public double Plasticity { get; init; }

    /// <summary>
    /// Evolvable learning decay in [0, 1]: how fast learned weights fade back toward the germline each
    /// tick (the stability–plasticity trade-off). 0 keeps learned changes indefinitely; higher values
    /// forget quickly and lean on the inherited baseline. Together with <see cref="Plasticity"/> this
    /// puts the <em>shape</em> of the learning rule — not just its rate — under selection. Founders start
    /// at 0. Only matters when the brain is plastic.
    /// </summary>
    public double LearningDecay { get; init; }

    /// <summary>
    /// Evolvable propensity to reproduce sexually in [0, 1]: 0 is obligate asexual cloning (today's
    /// behaviour), higher values make the organism attempt biparental reproduction — finding a willing
    /// adjacent mate and recombining germlines — when it reproduces. Founders start at 0 (sex is evolved
    /// in, like the defensive/learning traits), so an asexual baseline is always available and sex has to
    /// earn its keep against the two-fold cost. Falls back to cloning when no willing mate is adjacent.
    /// </summary>
    public double Sexuality { get; init; }

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
        MetabolicEfficiency = Clamp(MetabolicEfficiency, bounds.MetabolicEfficiency),
        Armour = Clamp(Armour, bounds.Armour),
        Evasion = Clamp(Evasion, bounds.Evasion),
        Toxicity = Clamp(Toxicity, bounds.Toxicity),
        Plasticity = Clamp(Plasticity, bounds.Plasticity),
        LearningDecay = Clamp(LearningDecay, bounds.LearningDecay),
        Sexuality = Clamp(Sexuality, bounds.Sexuality),
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
    /// Mid-range genome: the midpoint of every bound, except <see cref="CellCount"/> which starts at 1
    /// (unicellular, not the bound midpoint) with equal specialisation-weight midpoints, so the single
    /// cell is a generalist, and the evolved-in capabilities (metabolic efficiency, armour, evasion,
    /// toxicity, plasticity, sexuality) which start at their baseline (min) so they are earned by selection. Used
    /// as a neutral baseline; genesis founders use <see cref="Random"/>.
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
        MetabolicEfficiency = bounds.MetabolicEfficiency.Min,
        Armour = bounds.Armour.Min,
        Evasion = bounds.Evasion.Min,
        Toxicity = bounds.Toxicity.Min,
        Plasticity = bounds.Plasticity.Min,
        LearningDecay = bounds.LearningDecay.Min,
        Sexuality = bounds.Sexuality.Min,
        ShareFraction = Midpoint(bounds.ShareFraction),
        CellCount = bounds.CellCount.Min,
        GermWeight = Midpoint(bounds.GermWeight),
        FeederWeight = Midpoint(bounds.FeederWeight),
        StoreWeight = Midpoint(bounds.StoreWeight),
        DefenderWeight = Midpoint(bounds.DefenderWeight),
        MoverWeight = Midpoint(bounds.MoverWeight),
        SensorWeight = Midpoint(bounds.SensorWeight),
    };

    /// <summary>
    /// A fully randomised genome — each trait drawn uniformly within its bounds — for a *varied*
    /// founding gene pool rather than a clone army of identical <see cref="MidRange"/> founders, so
    /// the world feels alive and diverse from tick 0. Cell count is forced to 1 (founders are
    /// unicellular and evolve up) and the evolved-in capabilities (metabolic efficiency, armour, evasion,
    /// toxicity, plasticity, sexuality) to baseline 0 — earned by selection. Draws come from the supplied PRNG in a
    /// fixed trait order.
    /// </summary>
    public static Genome Random(TraitBounds bounds, Prng prng)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(prng);

        return new Genome
        {
            Size = Sample(bounds.Size, prng),
            SpeedCapacity = Sample(bounds.SpeedCapacity, prng),
            ThermalCenter = Sample(bounds.ThermalCenter, prng),
            ThermalWidth = Sample(bounds.ThermalWidth, prng),
            EnvRadius = Sample(bounds.EnvRadius, prng),
            OrgRadius = Sample(bounds.OrgRadius, prng),
            SensoryAcuity = Sample(bounds.SensoryAcuity, prng),
            MetabolicEfficiency = bounds.MetabolicEfficiency.Min,
            Armour = bounds.Armour.Min,
            Evasion = bounds.Evasion.Min,
            Toxicity = bounds.Toxicity.Min,
            Plasticity = bounds.Plasticity.Min,
            LearningDecay = bounds.LearningDecay.Min,
            Sexuality = bounds.Sexuality.Min,
            ShareFraction = Sample(bounds.ShareFraction, prng),
            CellCount = 1.0,
            GermWeight = Sample(bounds.GermWeight, prng),
            FeederWeight = Sample(bounds.FeederWeight, prng),
            StoreWeight = Sample(bounds.StoreWeight, prng),
            DefenderWeight = Sample(bounds.DefenderWeight, prng),
            MoverWeight = Sample(bounds.MoverWeight, prng),
            SensorWeight = Sample(bounds.SensorWeight, prng),
        };
    }

    private static double Sample(TraitBounds.Range range, Prng prng) => range.Min + (prng.NextDouble() * (range.Max - range.Min));

    private static double Clamp(double value, TraitBounds.Range range) =>
        Math.Clamp(value, range.Min, range.Max);

    private static double Midpoint(TraitBounds.Range range) => (range.Min + range.Max) / 2.0;
}
