using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class SimplexNoiseTests
{
    [Fact]
    public void SameSeed_sameCoordinate_sameValue()
    {
        var a = new SimplexNoise(42);
        var b = new SimplexNoise(42);

        for (double x = -5; x < 5; x += 0.37)
        {
            for (double y = -5; y < 5; y += 0.41)
            {
                Assert.Equal(a.Sample(x, y), b.Sample(x, y));
            }
        }
    }

    [Fact]
    public void DifferentSeeds_produceDifferentFields()
    {
        var a = new SimplexNoise(1);
        var b = new SimplexNoise(2);

        bool anyDifferent = false;
        for (double x = 0; x < 10 && !anyDifferent; x += 0.5)
        {
            anyDifferent = a.Sample(x, x * 0.5) != b.Sample(x, x * 0.5);
        }

        Assert.True(anyDifferent);
    }

    [Fact]
    public void Sample_staysInBoundedRange()
    {
        var noise = new SimplexNoise(123);
        for (double x = -50; x < 50; x += 0.13)
        {
            double v = noise.Sample(x, x * 1.7);
            Assert.InRange(v, -1.5, 1.5);
        }
    }

    [Fact]
    public void Fractal_isDeterministic_andBounded()
    {
        var noise = new SimplexNoise(555);
        var config = NoiseConfig.Default;

        for (double x = -20; x < 20; x += 0.7)
        {
            double v1 = noise.SampleFractal(x, -x, config);
            double v2 = noise.SampleFractal(x, -x, config);
            Assert.Equal(v1, v2);
            Assert.InRange(v1, -1.5, 1.5);
        }
    }
}
