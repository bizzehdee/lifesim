using System.Text.Json.Serialization;

namespace LifeSim.Console.Cli;

public sealed record ExperimentReport
{
    public long Ticks { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int InitialPopulation { get; init; }
    public List<ulong> Seeds { get; init; } = [];
    public ExperimentArmReport Baseline { get; init; } = new();
    public ExperimentArmReport Candidate { get; init; } = new();
    public PairedExperimentComparison Comparison { get; init; } = new();
}

public sealed record ExperimentArmReport
{
    public string Configuration { get; init; } = "default";
    public List<ExperimentRunResult> Runs { get; init; } = [];
    public ExperimentAggregate Aggregate { get; init; } = new();
}

public sealed record ExperimentRunResult
{
    public ulong Seed { get; init; }
    public long ExecutedTicks { get; init; }
    public bool Extinct { get; init; }
    public long FinalPopulation { get; init; }
    public double FinalEnergyAverage { get; init; }
    public long TotalBirths { get; init; }
    public long TotalDeaths { get; init; }
}

public sealed record ExperimentAggregate
{
    public ConfidenceInterval95 FinalPopulation { get; init; } = new();
    public ConfidenceInterval95 FinalEnergyAverage { get; init; } = new();
    public ConfidenceInterval95 ExecutedTicks { get; init; } = new();
    public ConfidenceInterval95 TotalBirths { get; init; } = new();
    public ProportionEstimate95 ExtinctionRate { get; init; } = new();
}

public sealed record PairedExperimentComparison
{
    public ConfidenceInterval95 FinalPopulationDifference { get; init; } = new();
    public ConfidenceInterval95 TotalBirthDifference { get; init; } = new();
    public double ExtinctionRateDifference { get; init; }
    public string Conclusion { get; init; } = "inconclusive";
    public string Evidence { get; init; } = "paired_multi_seed_95_percent_ci";
}

public sealed record ConfidenceInterval95
{
    public int SampleSize { get; init; }
    public double Mean { get; init; }
    public double StandardDeviation { get; init; }
    public double Lower { get; init; }
    public double Upper { get; init; }
}

public sealed record ProportionEstimate95
{
    public int SampleSize { get; init; }
    public int Count { get; init; }
    public double Rate { get; init; }
    public double Lower { get; init; }
    public double Upper { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(ExperimentReport))]
internal sealed partial class ExperimentReportJsonContext : JsonSerializerContext;
