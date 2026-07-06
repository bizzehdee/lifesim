using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class CombatTests
{
    [Fact]
    public void KillProbability_isHalf_whenSizesAreEqual()
    {
        Assert.Equal(0.5, Combat.KillProbability(5.0, 5.0));
    }

    [Fact]
    public void KillProbability_favorsTheLargerAttacker()
    {
        Assert.True(Combat.KillProbability(9.0, 1.0) > 0.5);
        Assert.True(Combat.KillProbability(1.0, 9.0) < 0.5);
    }

    [Theory]
    [InlineData(0.1, 0.1)]
    [InlineData(1.0, 100.0)]
    [InlineData(100.0, 1.0)]
    [InlineData(10.0, 10.0)]
    public void KillProbability_staysStrictlyBetweenZeroAndOne(double attackerSize, double victimSize)
    {
        double p = Combat.KillProbability(attackerSize, victimSize);
        Assert.InRange(p, 0.0001, 0.9999);
    }

    [Fact]
    public void KillProbability_isSymmetric_withRolesSwapped()
    {
        double attackerWins = Combat.KillProbability(3.0, 7.0);
        double victimWinsIfItAttacked = Combat.KillProbability(7.0, 3.0);
        Assert.Equal(1.0, attackerWins + victimWinsIfItAttacked, precision: 10);
    }
}
