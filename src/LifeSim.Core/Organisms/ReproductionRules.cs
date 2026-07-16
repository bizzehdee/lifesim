using LifeSim.Core.Configuration;

namespace LifeSim.Core.Organisms;

/// <summary>Authoritative derived reproduction gates shared by sensing, intent resolution, and presentation.</summary>
public static class ReproductionRules
{
    public static ReproductionReadiness Assess(
        Genome genome,
        double energy,
        long? lastBirthTick,
        long currentTick,
        SimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(genome);
        ArgumentNullException.ThrowIfNull(config);

        double cost = config.Reproduction.ReproductionBaseCost
            * Morphology.ReproductionMass(genome, config.Multicellular);
        bool fertile = Morphology.CanReproduce(genome, config.Multicellular);
        bool hasEnergy = energy >= cost;
        long cooldownRemaining = lastBirthTick is { } birth
            ? Math.Max(0, config.Reproduction.ReproductionCooldownTicks - (currentTick - birth))
            : 0;

        return new ReproductionReadiness(cost, fertile, hasEnergy, cooldownRemaining);
    }
}

/// <summary>The intrinsic reproduction gates; tile availability and mate booking are resolved later.</summary>
public sealed record ReproductionReadiness(
    double EnergyCost,
    bool Fertile,
    bool HasEnergy,
    long CooldownRemaining)
{
    public bool OffCooldown => CooldownRemaining == 0;

    public bool IsReady => Fertile && HasEnergy && OffCooldown;
}
