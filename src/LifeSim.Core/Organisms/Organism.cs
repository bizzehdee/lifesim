using LifeSim.Core.Neat;

namespace LifeSim.Core.Organisms;

/// <summary>
/// An organism instance: a dynamic state machine over an immutable <see cref="Genome"/> and an
/// evolvable <see cref="Brain"/>, occupying a single grid tile.
/// Removed from the simulation once its energy hits zero.
/// </summary>
public sealed class Organism
{
    /// <summary>The default energy ceiling for a single-cell body; multicellular bodies raise it via Store cells (§21).</summary>
    public const double EnergyCeiling = 100.0;

    /// <summary>This body's actual energy ceiling — <see cref="EnergyCeiling"/> for a plain cell, higher for a Store-rich body.</summary>
    public double EnergyCapacity { get; }

    public long Id { get; }

    public string Name { get; }

    public Genome Genome { get; }

    /// <summary>The live, working brain used for decisions each tick (its node states advance; under lifetime learning its weights may also change within life).</summary>
    public NeatGenome Brain { get; private set; }

    /// <summary>
    /// The inherited germline brain — the topology/weights this organism was born with, node-state
    /// zeroed. Reproduction mutates <em>this</em>, not the live brain, so learned changes are not
    /// inherited (Darwinian; the Baldwin effect works through selection on the germline). With no
    /// lifetime learning its weights equal <see cref="Brain"/>'s.
    /// </summary>
    public NeatGenome Germline { get; }

    public double Energy { get; private set; }

    public long Age { get; private set; }

    public int X { get; private set; }

    public int Y { get; private set; }

    /// <summary>The action selected last tick; null before the organism's first decision.</summary>
    public OrganismAction? LastAction { get; private set; }

    /// <summary>The outcome of <see cref="LastAction"/>, fed back in as a sensory input.</summary>
    public ActionResult LastActionResult { get; private set; }

    /// <summary>The tick of this organism's most recent successful birth; null if it has never reproduced.</summary>
    public long? LastBirthTick { get; private set; }

    /// <summary>Lifetime kills — how many other organisms this one has successfully preyed on.</summary>
    public long PredationWins { get; private set; }

    /// <summary>Lifetime failed hunts — predation attempts that were fought off (the attacker survived, took the retaliation penalty).</summary>
    public long PredationLosses { get; private set; }

    /// <summary>Lifetime predation attempts against live organisms (<see cref="PredationWins"/> + <see cref="PredationLosses"/>).</summary>
    public long PredationAttempts => PredationWins + PredationLosses;

    /// <summary>
    /// Lifetime relatedness-weighted energy this organism has donated via sharing — its indirect-fitness
    /// contribution (Σ energyDonated × relatedness-to-recipient). A lifetime tally like the predation
    /// record; not inherited.
    /// </summary>
    public double HelpGiven { get; private set; }

    /// <summary>Explanation of the most recent chosen action; null before the first decision.</summary>
    public DecisionTrace? LastDecisionTrace { get; private set; }

    public bool IsAlive => Energy > 0.0;

    public Organism(
        long id, Genome genome, string name, double energy, int x, int y, NeatGenome brain,
        long age = 0, OrganismAction? lastAction = null, ActionResult lastActionResult = ActionResult.None,
        long? lastBirthTick = null, double? energyCapacity = null, long predationWins = 0, long predationLosses = 0,
        double helpGiven = 0.0, NeatGenome? germline = null, DecisionTrace? lastDecisionTrace = null)
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
        Germline = (germline ?? brain).ResetState(); // inherited weights, always node-state-zeroed
        Age = age;
        LastAction = lastAction;
        LastActionResult = lastActionResult;
        LastBirthTick = lastBirthTick;
        PredationWins = predationWins;
        PredationLosses = predationLosses;
        HelpGiven = helpGiven;
        LastDecisionTrace = lastDecisionTrace;
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

    public void RecordDecisionTrace(DecisionTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        LastDecisionTrace = trace;
    }

    public void RecordLearningReward(double reward)
    {
        if (LastDecisionTrace is not null)
        {
            LastDecisionTrace = LastDecisionTrace with { LearningReward = reward };
        }
    }

    public void RecordBirth(long tick) => LastBirthTick = tick;

    /// <summary>Tally a successful hunt (a kill).</summary>
    public void RecordPredationWin() => PredationWins++;

    /// <summary>Tally a failed hunt (the target fought it off).</summary>
    public void RecordPredationLoss() => PredationLosses++;

    /// <summary>Accumulate a share's relatedness-weighted energy toward this organism's indirect fitness.</summary>
    public void RecordHelpGiven(double relatednessWeightedEnergy)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(relatednessWeightedEnergy);
        HelpGiven += relatednessWeightedEnergy;
    }

    public void UpdateBrain(NeatGenome brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        Brain = brain;
    }
}
