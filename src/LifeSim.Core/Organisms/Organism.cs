using LifeSim.Core.Neat;

namespace LifeSim.Core.Organisms;

/// <summary>
/// An organism instance: a dynamic state machine over an immutable <see cref="Genome"/> and an
/// evolvable <see cref="Brain"/>, occupying a single grid tile (lifesim.md §3, §4, §10, §11).
/// Removed from the simulation once its energy hits zero.
/// </summary>
public sealed class Organism
{
    /// <summary>The hard energy ceiling every organism is clamped to (lifesim.md §3).</summary>
    public const double EnergyCeiling = 100.0;

    public long Id { get; }

    public string Name { get; }

    public Genome Genome { get; }

    public NeatGenome Brain { get; private set; }

    public double Energy { get; private set; }

    public long Age { get; private set; }

    public int X { get; private set; }

    public int Y { get; private set; }

    /// <summary>The action selected last tick (lifesim.md §12); null before the organism's first decision.</summary>
    public OrganismAction? LastAction { get; private set; }

    public bool IsAlive => Energy > 0.0;

    public Organism(
        long id, Genome genome, string name, double energy, int x, int y, NeatGenome brain,
        long age = 0, OrganismAction? lastAction = null)
    {
        ArgumentNullException.ThrowIfNull(genome);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(brain);

        Id = id;
        Genome = genome;
        Name = name;
        Energy = Math.Clamp(energy, 0.0, EnergyCeiling);
        X = x;
        Y = y;
        Brain = brain;
        Age = age;
        LastAction = lastAction;
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

    public void MoveTo(int x, int y)
    {
        X = x;
        Y = y;
    }

    public void RecordAction(OrganismAction action) => LastAction = action;

    public void UpdateBrain(NeatGenome brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        Brain = brain;
    }
}
