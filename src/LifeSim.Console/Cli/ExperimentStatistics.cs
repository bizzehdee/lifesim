namespace LifeSim.Console.Cli;

public static class ExperimentStatistics
{
    // Two-sided 95% Student-t critical values for df 1..30; normal approximation thereafter.
    private static readonly double[] TCritical95 =
    [
        0.0, 12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262,
        2.228, 2.201, 2.179, 2.160, 2.145, 2.131, 2.120, 2.110, 2.101, 2.093,
        2.086, 2.080, 2.074, 2.069, 2.064, 2.060, 2.056, 2.052, 2.048, 2.045, 2.042,
    ];

    public static ConfidenceInterval95 Mean95(IEnumerable<double> observations)
    {
        double[] values = observations.ToArray();
        if (values.Length == 0)
        {
            return new ConfidenceInterval95();
        }

        double mean = values.Average();
        if (values.Length == 1)
        {
            return new ConfidenceInterval95 { SampleSize = 1, Mean = mean, Lower = mean, Upper = mean };
        }

        double variance = values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1);
        double standardDeviation = Math.Sqrt(variance);
        int degreesOfFreedom = values.Length - 1;
        double critical = degreesOfFreedom < TCritical95.Length ? TCritical95[degreesOfFreedom] : 1.96;
        double margin = critical * standardDeviation / Math.Sqrt(values.Length);
        return new ConfidenceInterval95
        {
            SampleSize = values.Length,
            Mean = mean,
            StandardDeviation = standardDeviation,
            Lower = mean - margin,
            Upper = mean + margin,
        };
    }

    /// <summary>Wilson score interval, which remains meaningful for zero or all observed extinctions.</summary>
    public static ProportionEstimate95 Proportion95(int count, int sampleSize)
    {
        if (sampleSize <= 0 || count < 0 || count > sampleSize)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleSize));
        }

        const double z = 1.96;
        double rate = count / (double)sampleSize;
        double denominator = 1.0 + ((z * z) / sampleSize);
        double center = (rate + ((z * z) / (2.0 * sampleSize))) / denominator;
        double margin = z * Math.Sqrt(((rate * (1.0 - rate)) / sampleSize) + ((z * z) / (4.0 * sampleSize * sampleSize))) / denominator;
        return new ProportionEstimate95
        {
            SampleSize = sampleSize,
            Count = count,
            Rate = rate,
            Lower = Math.Max(0.0, center - margin),
            Upper = Math.Min(1.0, center + margin),
        };
    }
}
