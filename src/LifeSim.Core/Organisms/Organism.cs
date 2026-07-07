using LifeSim.Core.Neat;

namespace LifeSim.Core.Organisms;

/// <summary>
/// An organism instance: a dynamic state machine over an immutable <see cref="Genome"/> and an
/// evolvable <see cref="Brain"/>, occupying a single grid tile (lifesim.md §3, §4, §10, §11).
/// Removed from the simulation once its energy hits zero.
/// </summary>
public sealed class Organism
{
    /// <summary>The default energy ceiling for a single-cell body (lifesim.md §3); multicellular bodies raise it via Store cells (§21).</summary>
    public const double EnergyCeiling = 100.0;

    /// <summary>This body's actual energy ceiling — <see cref="EnergyCeiling"/> for a plain cell, higher for a Store-rich body (lifesim.md §21).</summary>
    public double EnergyCapacity { get; }

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

    /// <summary>The outcome of <see cref="LastAction"/> (lifesim.md §12, §13), fed back in as a sensory input.</summary>
    public ActionResult LastActionResult { get; private set; }

    /// <summary>The tick of this organism's most recent successful birth; null if it has never reproduced (lifesim.md §8, §12, §17).</summary>
    public long? LastBirthTick { get; private set; }

    public bool IsAlive => Energy > 0.0;

    public Organism(
        long id, Genome genome, string name, double energy, int x, int y, NeatGenome brain,
        long age = 0, OrganismAction? lastAction = null, ActionResult lastActionResult = ActionResult.None,
        long? lastBirthTick = null, double? energyCapacity = null)
    {
        ArgumentNullException.ThrowIfNull(genome);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(brain);

        Id = id;
        Genome = genome;
        Name = name;
        EnergyCapacity = energyCapacity ?? EnergyCeiling;
        Energy = Math.Clamp(energy, 0.0, EnergyCapacity);
        X = x;
        Y = y;
        Brain = brain;
        Age = age;
        LastAction = lastAction;
        LastActionResult = lastActionResult;
        LastBirthTick = lastBirthTick;
    }

    public void AddEnergy(double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Energy = Math.Min(EnergyCapacity, Energy + amount);
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

    public void RecordActionResult(ActionResult result) => LastActionResult = result;

    public void RecordBirth(long tick) => LastBirthTick = tick;

    public void UpdateBrain(NeatGenome brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        Brain = brain;
    }
}
