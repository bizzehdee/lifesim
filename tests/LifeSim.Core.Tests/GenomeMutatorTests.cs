using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class GenomeMutatorTests
{
    private static readonly TraitBounds Bounds = new();

    private static Genome MidRange() => Genome.MidRange(Bounds);

    [Fact]
    public void Mutate_withZeroRate_leavesEveryTraitUnchanged()
    {
        MutationConfig config = new MutationConfig() with { TraitMutationRate = 0.0 };
        Genome original = MidRange();

        Genome mutated = GenomeMutator.Mutate(original, config, Bounds, new Prng(1));

        Assert.Equal(original, mutated);
    }

    [Fact]
    public void Mutate_withFullRate_perturbsTraits()
    {
        MutationConfig config = new MutationConfig() with { TraitMutationRate = 1.0, TraitMutationDelta = 0.05 };
        Genome original = MidRange();

        Genome mutated = GenomeMutator.Mutate(original, config, Bounds, new Prng(1));

        Assert.NotEqual(original, mutated);
    }

    [Fact]
    public void Mutate_isDeterministicForTheSamePrngState()
    {
        MutationConfig config = new();
        Genome original = MidRange();

        Genome a = GenomeMutator.Mutate(original, config, Bounds, new Prng(4242));
        Genome b = GenomeMutator.Mutate(original, config, Bounds, new Prng(4242));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Mutate_alwaysKeepsTraitsWithinBounds_evenUnderHugeDeltas()
    {
        // Deltas far larger than any bound span, applied every trait, must still clamp.
        MutationConfig config = new MutationConfig() with { TraitMutationRate = 1.0, TraitMutationDelta = 100.0 };

        // Start already pinned to the maximum so upward deltas would overshoot without clamping.
        Genome extreme = new()
        {
            Size = Bounds.Size.Max,
            SpeedCapacity = Bounds.SpeedCapacity.Max,
            ThermalCenter = Bounds.ThermalCenter.Max,
            ThermalWidth = Bounds.ThermalWidth.Max,
            EnvRadius = Bounds.EnvRadius.Max,
            OrgRadius = Bounds.OrgRadius.Max,
            SensoryAcuity = Bounds.SensoryAcuity.Max,
            ShareFraction = Bounds.ShareFraction.Max,
        };

        for (var seed = 1UL; seed <= 50; seed++)
        {
            Genome mutated = GenomeMutator.Mutate(extreme, config, Bounds, new Prng(seed));

            Assert.InRange(mutated.Size, Bounds.Size.Min, Bounds.Size.Max);
            Assert.InRange(mutated.SpeedCapacity, Bounds.SpeedCapacity.Min, Bounds.SpeedCapacity.Max);
            Assert.InRange(mutated.ThermalCenter, Bounds.ThermalCenter.Min, Bounds.ThermalCenter.Max);
            Assert.InRange(mutated.ThermalWidth, Bounds.ThermalWidth.Min, Bounds.ThermalWidth.Max);
            Assert.InRange(mutated.EnvRadius, Bounds.EnvRadius.Min, Bounds.EnvRadius.Max);
            Assert.InRange(mutated.OrgRadius, Bounds.OrgRadius.Min, Bounds.OrgRadius.Max);
            Assert.InRange(mutated.SensoryAcuity, Bounds.SensoryAcuity.Min, Bounds.SensoryAcuity.Max);
            Assert.InRange(mutated.ShareFraction, Bounds.ShareFraction.Min, Bounds.ShareFraction.Max);
        }
    }

    [Fact]
    public void Mutate_deltaScalesWithTheTraitsOwnBoundSpan()
    {
        // Deterministic delta: a single trait mutating once moves by at most delta * span.
        MutationConfig config = new MutationConfig() with { TraitMutationRate = 1.0, TraitMutationDelta = 0.05 };
        Genome original = MidRange();

        double sizeSpan = Bounds.Size.Max - Bounds.Size.Min;
        double thermalSpan = Bounds.ThermalCenter.Max - Bounds.ThermalCenter.Min;

        double maxSizeDrift = 0.0;
        double maxThermalDrift = 0.0;
        for (var seed = 1UL; seed <= 200; seed++)
        {
            Genome mutated = GenomeMutator.Mutate(original, config, Bounds, new Prng(seed));
            maxSizeDrift = Math.Max(maxSizeDrift, Math.Abs(mutated.Size - original.Size));
            maxThermalDrift = Math.Max(maxThermalDrift, Math.Abs(mutated.ThermalCenter - original.ThermalCenter));
        }

        Assert.True(maxSizeDrift <= config.TraitMutationDelta * sizeSpan + 1e-9);
        Assert.True(maxThermalDrift <= config.TraitMutationDelta * thermalSpan + 1e-9);

        // The wider-range trait must be capable of a proportionally larger absolute step.
        Assert.True(maxThermalDrift > maxSizeDrift);
    }
}
