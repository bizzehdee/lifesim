using LifeSim.Core.Configuration;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class MorphologyTests
{
    private static readonly MulticellularConfig Enabled = new();
    private static readonly MulticellularConfig Disabled = new() { Enabled = false };

    private static Genome Cell(double count = 1.0, double germ = 0, double feeder = 0, double store = 0,
        double defender = 0, double mover = 0, double sensor = 0, double size = 2.0) => new()
        {
            Size = size,
            CellCount = count,
            GermWeight = germ,
            FeederWeight = feeder,
            StoreWeight = store,
            DefenderWeight = defender,
            MoverWeight = mover,
            SensorWeight = sensor,
            SensoryAcuity = 0.4,
        };

    [Fact]
    public void Disabled_collapsesEveryBodyToOneGeneralistCell()
    {
        // With multicellularity off, a body with a big cell-count/specialisation genome behaves like a plain cell.
        Genome fancy = Cell(count: 20, feeder: 1.0, store: 1.0);

        Assert.Equal(1.0, Morphology.CellCount(fancy, Disabled));
        Assert.Equal(fancy.Size, Morphology.Mass(fancy, Disabled));
        Assert.Equal(Disabled.BaseCapacity, Morphology.Capacity(fancy, Disabled));
        Assert.Equal(0.0, Morphology.CoordinationCost(fancy, Disabled));
        Assert.Equal(1.0, Morphology.FeedMultiplier(fancy, Disabled)); // no bonus when disabled
        Assert.True(Morphology.CanReproduce(fancy, Disabled));         // fertility gate is off
    }

    [Fact]
    public void GeneralistOneCellBody_isNeutral_matchingAPlainOrganism()
    {
        // Equal weights => 1/6 each => every "excess over baseline" is zero => all effects neutral.
        Genome generalist = Cell(count: 1, germ: 0.5, feeder: 0.5, store: 0.5, defender: 0.5, mover: 0.5, sensor: 0.5);

        Assert.Equal(1.0, Morphology.CellCount(generalist, Enabled));
        Assert.Equal(generalist.Size, Morphology.Mass(generalist, Enabled), precision: 10);
        Assert.Equal(Enabled.BaseCapacity, Morphology.Capacity(generalist, Enabled), precision: 10);
        Assert.Equal(0.0, Morphology.CoordinationCost(generalist, Enabled), precision: 10);
        Assert.Equal(1.0, Morphology.FeedMultiplier(generalist, Enabled), precision: 10);
        Assert.Equal(generalist.Size, Morphology.CombatMass(generalist, Enabled), precision: 10);
        Assert.Equal(1.0, Morphology.ThermalStressFactor(generalist, Enabled), precision: 10);
        Assert.Equal(1.0, Morphology.LocomotionFactor(generalist, Enabled), precision: 10);
    }

    [Fact]
    public void ZeroWeights_fallBackToGeneralistSplit()
    {
        Morphology.CellFractions f = Morphology.Fractions(Cell(count: 4), Enabled);
        Assert.Equal(Morphology.GeneralistShare, f.Germ, precision: 10);
        Assert.Equal(Morphology.GeneralistShare, f.Sensor, precision: 10);
    }

    [Fact]
    public void Mass_isCellsTimesSize()
    {
        Assert.Equal(10 * 2.0, Morphology.Mass(Cell(count: 10, size: 2.0), Enabled), precision: 10);
    }

    [Fact]
    public void SquareCubeLaw_intakeGrowsSlowerThanUpkeep_soPerCellDeficitRisesWithSize()
    {
        // Maintenance ~ volume (∝ N); grazing intake caps at ~ surface (∝ N^2/3). The ratio of max
        // sustainable intake to upkeep must fall as the body grows — the size limiter (lifesim.md §21).
        double RatioAt(double n)
        {
            Genome g = Cell(count: n);
            double upkeep = (Metabolism.BaseMetabolism(g, new MetabolismConfig()) * n) + Morphology.CoordinationCost(g, Enabled);
            double intake = Morphology.MaxGrazingIntake(g, Enabled);
            return intake / upkeep;
        }

        Assert.True(RatioAt(2) > RatioAt(8));
        Assert.True(RatioAt(8) > RatioAt(24));
    }

    [Fact]
    public void StoreCells_raiseCapacity_aboveTheGeneralistBaseline()
    {
        double baseline = Morphology.Capacity(Cell(count: 10), Enabled);           // generalist
        double stored = Morphology.Capacity(Cell(count: 10, store: 1.0), Enabled);  // all-store emphasis
        Assert.True(stored > baseline);
        Assert.Equal(Enabled.BaseCapacity, baseline, precision: 10);               // generalist == base
    }

    [Fact]
    public void DefenderCells_raiseCombatMassAndInsulate()
    {
        Genome defender = Cell(count: 6, defender: 1.0);
        Assert.True(Morphology.CombatMass(defender, Enabled) > Morphology.Mass(defender, Enabled));
        Assert.True(Morphology.ThermalStressFactor(defender, Enabled) < 1.0);
    }

    [Fact]
    public void MoverCells_cutLocomotion_andSensorCells_sharpenAcuity()
    {
        Assert.True(Morphology.LocomotionFactor(Cell(count: 6, mover: 1.0), Enabled) < 1.0);
        Genome sensor = Cell(count: 6, sensor: 1.0);
        Assert.True(Morphology.EffectiveAcuity(sensor, Enabled) > sensor.SensoryAcuity);
        Assert.True(Morphology.EffectiveAcuity(sensor, Enabled) <= 1.0);
    }

    [Fact]
    public void OffspringGrowthBias_nudgesMulticellularOffspringUpward_butLeavesUnicellularAndDisabledAlone()
    {
        // Default bias 0.5: a 5-cell parent adds 0.5 * (5 - 1) = 2 to its offspring's mutated cell count.
        Assert.Equal(6.0, Morphology.BiasedOffspringCellCount(offspringCellCount: 4.0, parentCellCount: 5.0, Enabled), precision: 10);

        // A unicellular parent gets no bias — multicellularity only reinforces itself once it exists.
        Assert.Equal(4.0, Morphology.BiasedOffspringCellCount(4.0, 1.0, Enabled), precision: 10);

        // No bias when multicellularity is disabled.
        Assert.Equal(4.0, Morphology.BiasedOffspringCellCount(4.0, 5.0, Disabled), precision: 10);
    }

    [Fact]
    public void SpecialistCount_countsTypesAboveTheGeneralistBaseline()
    {
        Assert.Equal(0, Morphology.SpecialistCount(Cell(count: 6), Enabled));                       // generalist
        Assert.Equal(1, Morphology.SpecialistCount(Cell(count: 6, feeder: 1.0), Enabled));          // all feeder
        Assert.Equal(3, Morphology.SpecialistCount(Cell(count: 6, germ: 1.0, feeder: 1.0, store: 1.0), Enabled));
    }

    [Fact]
    public void DivisionOfLabour_makesDiverseBodiesCheaperThanLopsidedOnesOfTheSameSize()
    {
        const double perCellBase = 0.1;
        Genome lopsided = Cell(count: 8, feeder: 1.0);                                              // 1 specialist
        Genome diverse = Cell(count: 8, germ: 1.0, feeder: 1.0, store: 1.0, defender: 1.0);         // 4 specialists

        Assert.Equal(0.0, Morphology.LaborEfficiency(lopsided, Enabled), precision: 10);
        Assert.Equal(Enabled.DivisionOfLabourDiscount, Morphology.LaborEfficiency(diverse, Enabled), precision: 10);
        Assert.True(Morphology.MulticellularOverhead(diverse, perCellBase, Enabled)
            < Morphology.MulticellularOverhead(lopsided, perCellBase, Enabled));
    }

    [Fact]
    public void MulticellularOverhead_isZeroForASingleCell_andUndiscountedWithoutSpecialists()
    {
        Assert.Equal(0.0, Morphology.MulticellularOverhead(Cell(count: 1), 0.1, Enabled), precision: 10);

        // A generalist multicellular body (no specialists) gets no discount: raw (N-1)*(base+coord).
        Genome generalist = Cell(count: 5);
        double raw = (5 - 1) * (0.1 + Enabled.CoordinationCostPerCell);
        Assert.Equal(raw, Morphology.MulticellularOverhead(generalist, 0.1, Enabled), precision: 10);
    }

    [Fact]
    public void BrainSteps_growWithCellCount_upToTheCap()
    {
        Assert.Equal(1, Morphology.BrainSteps(Cell(count: 1), Enabled));   // single cell → base model
        Assert.Equal(1, Morphology.BrainSteps(Cell(count: 20), Disabled)); // no bonus when disabled

        // Default 0.5 per extra cell: 3 cells → 1 + floor(2*0.5) = 2 steps; 5 → 3.
        Assert.Equal(2, Morphology.BrainSteps(Cell(count: 3), Enabled));
        Assert.Equal(3, Morphology.BrainSteps(Cell(count: 5), Enabled));

        // Capped at MaxNeuralSteps.
        Assert.Equal(Enabled.MaxNeuralSteps, Morphology.BrainSteps(Cell(count: 32), Enabled));
    }

    [Fact]
    public void GermCells_gateFertility_sterileSomaCannotReproduce()
    {
        // A body that invests everything except germ (germ fraction below the threshold) is sterile.
        Genome soma = Cell(count: 10, feeder: 1.0, store: 1.0, defender: 1.0, mover: 1.0, sensor: 1.0, germ: 0.0);
        Assert.False(Morphology.CanReproduce(soma, Enabled));

        Genome germline = Cell(count: 10, germ: 1.0);
        Assert.True(Morphology.CanReproduce(germline, Enabled));
    }
}
