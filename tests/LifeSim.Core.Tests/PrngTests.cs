using LifeSim.Core.Determinism;

namespace LifeSim.Core.Tests;

public class PrngTests
{
    [Fact]
    public void SameSeed_producesIdenticalSequence()
    {
        var a = new Prng(12345);
        var b = new Prng(12345);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextULong(), b.NextULong());
        }
    }

    [Fact]
    public void DifferentSeeds_diverge()
    {
        var a = new Prng(1);
        var b = new Prng(2);

        bool anyDifferent = false;
        for (int i = 0; i < 100 && !anyDifferent; i++)
        {
            anyDifferent = a.NextULong() != b.NextULong();
        }

        Assert.True(anyDifferent);
    }

    [Fact]
    public void NextDouble_isInUnitInterval()
    {
        var rng = new Prng(99);
        for (int i = 0; i < 100_000; i++)
        {
            double d = rng.NextDouble();
            Assert.InRange(d, 0.0, 0.9999999999999999);
        }
    }

    [Fact]
    public void NextInt_isInRange_andCoversBounds()
    {
        var rng = new Prng(7);
        bool sawZero = false;
        bool sawMax = false;
        for (int i = 0; i < 100_000; i++)
        {
            int v = rng.NextInt(10);
            Assert.InRange(v, 0, 9);
            sawZero |= v == 0;
            sawMax |= v == 9;
        }

        Assert.True(sawZero && sawMax);
    }

    [Fact]
    public void State_capturedAndRestored_continuationIsIdentical()
    {
        var rng = new Prng(2024);
        for (int i = 0; i < 50; i++)
        {
            rng.NextULong();
        }

        ulong[] state = rng.GetState();
        var restored = Prng.FromState(state);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(rng.NextULong(), restored.NextULong());
        }
    }

    [Fact]
    public void FromState_rejectsWrongLength()
    {
        Assert.Throws<ArgumentException>(() => Prng.FromState([1, 2, 3]));
    }
}
