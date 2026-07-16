using LifeSim.Core.Organisms;

namespace LifeSim.Core.Neat;

/// <summary>Compact explanation of the most recent brain decision.</summary>
public sealed record DecisionTrace
{
    public long Tick { get; init; }
    public OrganismAction ChosenAction { get; init; }
    public List<double> ActionProbabilities { get; init; } = [];
    public List<DecisionInputSignal> StrongestInputs { get; init; } = [];
    public List<DecisionContribution> StrongestContributions { get; init; } = [];
    public double? LearningReward { get; init; }

    public bool Equals(DecisionTrace? other) =>
        other is not null
        && Tick == other.Tick
        && ChosenAction == other.ChosenAction
        && Nullable.Equals(LearningReward, other.LearningReward)
        && ActionProbabilities.SequenceEqual(other.ActionProbabilities)
        && StrongestInputs.SequenceEqual(other.StrongestInputs)
        && StrongestContributions.SequenceEqual(other.StrongestContributions);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Tick);
        hash.Add(ChosenAction);
        hash.Add(LearningReward);
        foreach (double probability in ActionProbabilities)
        {
            hash.Add(probability);
        }

        foreach (DecisionInputSignal input in StrongestInputs)
        {
            hash.Add(input);
        }

        foreach (DecisionContribution contribution in StrongestContributions)
        {
            hash.Add(contribution);
        }

        return hash.ToHashCode();
    }
}

/// <summary>A high-magnitude sensory value held during the decision.</summary>
public sealed record DecisionInputSignal(int InputIndex, double Value);

/// <summary>An enabled edge's signed contribution to the chosen output on the final recurrent step.</summary>
public sealed record DecisionContribution
{
    public long SourceNodeId { get; init; }
    public NodeType SourceNodeType { get; init; }
    public double SourceActivation { get; init; }
    public double Weight { get; init; }
    public double WeightedSignal { get; init; }
}
