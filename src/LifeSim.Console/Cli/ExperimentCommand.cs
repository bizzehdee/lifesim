using System.Globalization;
using System.Text.Json;
using LifeSim.Core.Configuration;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Console.Cli;

/// <summary>Runs a paired baseline/candidate experiment over the same seed panel.</summary>
public static class ExperimentCommand
{
    private const string DefaultSeeds = "1,2,3,4,5,6,7,8,9,10";
    private const int MinimumSeeds = 5;

    public static int Execute(CommandLine args, TextWriter output)
    {
        string candidatePath = args.GetRequired("candidate");
        string outPath = args.GetRequired("out");
        string? baselinePath = args.GetString("baseline");
        long ticks = args.GetLong("ticks", 500);
        int width = args.GetInt("width", 64);
        int height = args.GetInt("height", 64);
        int population = args.GetInt("population", 100);
        if (ticks <= 0 || width <= 0 || height <= 0 || population <= 0)
        {
            throw new CommandLineException("--ticks, --width, --height, and --population must be > 0.");
        }

        List<ulong> seeds = ParseSeeds(args.GetString("seeds") ?? DefaultSeeds);
        if (seeds.Count < MinimumSeeds)
        {
            throw new CommandLineException($"--seeds requires at least {MinimumSeeds} distinct seeds for a multi-seed conclusion.");
        }

        SimulationConfig baseline = baselinePath is null
            ? SimulationConfig.Default
            : SnapshotSerializer.LoadConfig(File.ReadAllText(baselinePath));
        SimulationConfig candidate = SnapshotSerializer.LoadConfig(File.ReadAllText(candidatePath));
        baseline = baseline with { InitialPopulation = population };
        candidate = candidate with { InitialPopulation = population };
        int threads = ThreadOption.Resolve(args);

        List<ExperimentRunResult> baselineRuns = seeds
            .Select(seed => Run(seed, ticks, width, height, baseline, threads))
            .ToList();
        List<ExperimentRunResult> candidateRuns = seeds
            .Select(seed => Run(seed, ticks, width, height, candidate, threads))
            .ToList();

        ExperimentAggregate baselineAggregate = Aggregate(baselineRuns);
        ExperimentAggregate candidateAggregate = Aggregate(candidateRuns);
        ConfidenceInterval95 populationDifference = ExperimentStatistics.Mean95(
            candidateRuns.Zip(baselineRuns, (candidateRun, baselineRun) =>
                (double)(candidateRun.FinalPopulation - baselineRun.FinalPopulation)));
        ConfidenceInterval95 birthDifference = ExperimentStatistics.Mean95(
            candidateRuns.Zip(baselineRuns, (candidateRun, baselineRun) =>
                (double)(candidateRun.TotalBirths - baselineRun.TotalBirths)));

        string conclusion = populationDifference.Lower > 0.0
            ? "candidate_higher_final_population"
            : populationDifference.Upper < 0.0
                ? "candidate_lower_final_population"
                : "inconclusive_final_population";

        var report = new ExperimentReport
        {
            Ticks = ticks,
            Width = width,
            Height = height,
            InitialPopulation = population,
            Seeds = seeds,
            Baseline = new ExperimentArmReport
            {
                Configuration = baselinePath ?? "default",
                Runs = baselineRuns,
                Aggregate = baselineAggregate,
            },
            Candidate = new ExperimentArmReport
            {
                Configuration = candidatePath,
                Runs = candidateRuns,
                Aggregate = candidateAggregate,
            },
            Comparison = new PairedExperimentComparison
            {
                FinalPopulationDifference = populationDifference,
                TotalBirthDifference = birthDifference,
                ExtinctionRateDifference = candidateAggregate.ExtinctionRate.Rate - baselineAggregate.ExtinctionRate.Rate,
                Conclusion = conclusion,
            },
        };

        File.WriteAllText(outPath, JsonSerializer.Serialize(report, ExperimentReportJsonContext.Default.ExperimentReport));
        output.WriteLine(
            $"Paired experiment: {seeds.Count} seeds × 2 arms × {ticks} ticks; {conclusion} -> {outPath}");
        return 0;
    }

    private static List<ulong> ParseSeeds(string value)
    {
        var seeds = new List<ulong>();
        foreach (string token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong seed))
            {
                throw new CommandLineException("--seeds must be a comma-separated list of non-negative integers.");
            }

            if (!seeds.Contains(seed))
            {
                seeds.Add(seed);
            }
        }

        return seeds;
    }

    private static ExperimentRunResult Run(
        ulong seed,
        long requestedTicks,
        int width,
        int height,
        SimulationConfig config,
        int threads)
    {
        SimulationWorld world = SimulationWorld.CreateGenesis(
            new WorldState { Seed = seed, Width = width, Height = height }, config);
        world.MaxDegreeOfParallelism = threads;
        long births = 0, deaths = 0, executed = 0;
        while (executed < requestedTicks && !world.Extinct)
        {
            world.Advance();
            executed++;
            births += world.Metrics.Births;
            deaths += world.Metrics.Deaths;
        }

        return new ExperimentRunResult
        {
            Seed = seed,
            ExecutedTicks = executed,
            Extinct = world.Extinct,
            FinalPopulation = world.Metrics.Population,
            FinalEnergyAverage = world.Metrics.EnergyAverage,
            TotalBirths = births,
            TotalDeaths = deaths,
        };
    }

    private static ExperimentAggregate Aggregate(List<ExperimentRunResult> runs) => new()
    {
        FinalPopulation = ExperimentStatistics.Mean95(runs.Select(run => (double)run.FinalPopulation)),
        FinalEnergyAverage = ExperimentStatistics.Mean95(runs.Select(run => run.FinalEnergyAverage)),
        ExecutedTicks = ExperimentStatistics.Mean95(runs.Select(run => (double)run.ExecutedTicks)),
        TotalBirths = ExperimentStatistics.Mean95(runs.Select(run => (double)run.TotalBirths)),
        ExtinctionRate = ExperimentStatistics.Proportion95(runs.Count(run => run.Extinct), runs.Count),
    };
}
