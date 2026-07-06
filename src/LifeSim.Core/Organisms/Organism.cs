namespace LifeSim.Core.Organisms;

/// <summary>
/// An organism instance: a dynamic state machine over an immutable <see cref="Genome"/>
/// (lifesim.md §3, §11). Removed from the simulation once its energy hits zero.
/// </summary>
public sealed class Organism
{
    /// <summary>The hard energy ceiling every organism is clamped to (lifesim.md §3).</summary>
    public const double EnergyCeiling = 100.0;

    public long Id { get; }

    public string Name { get; }

    public Genome Genome { get; }

    public double Energy { get; private set; }

    public long Age { get; private set; }

    public bool IsAlive => Energy > 0.0;

    public Organism(long id, Genome genome, string name, double initialEnergy)
    {
        ArgumentNullException.ThrowIfNull(genome);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Genome = genome;
        Name = name;
        Energy = Math.Clamp(initialEnergy, 0.0, EnergyCeiling);
    }

    public void AddEnergy(double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Energy = Math.Min(EnergyCeiling, Energy + amount);
    }

    /// <summary>Removes up to <paramref name="amount"/> energy, clamped at zero; returns what was actually spent.</summary>
    public double SpendEnergy(double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        double spent = Math.Min(Energy, amount);
        Energy -= spent;
        return spent;
    }

    public void Tick() => Age++;
}
