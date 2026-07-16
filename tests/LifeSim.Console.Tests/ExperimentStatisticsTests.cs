using LifeSim.Console.Cli;

namespace LifeSim.Console.Tests;

public class ExperimentStatisticsTests
{
    [Fact]
    public void Mean95_usesSampleVariationAndContainsTheMean()
    {
        ConfidenceInterval95 result = ExperimentStatistics.Mean95([1.0, 2.0, 3.0, 4.0, 5.0]);

        Assert.Equal(5, result.SampleSize);
        Assert.Equal(3.0, result.Mean);
        Assert.True(result.StandardDeviation > 0.0);
        Assert.True(result.Lower < result.Mean);
        Assert.True(result.Upper > result.Mean);
    }

    [Fact]
    public void WilsonInterval_reportsUncertaintyEvenWithNoObservedExtinctions()
    {
        ProportionEstimate95 result = ExperimentStatistics.Proportion95(count: 0, sampleSize: 10);

        Assert.Equal(0.0, result.Rate);
        Assert.Equal(0.0, result.Lower);
        Assert.True(result.Upper > 0.0);
    }
}
