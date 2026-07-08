using LifeSim.Core.Configuration;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class CombatTests
{
    private static readonly MovementCombatConfig Combatant = new();

    private static Genome Defender(double armour = 0, double evasion = 0, double toxicity = 0) =>
        new() { Armour = armour, Evasion = evasion, Toxicity = toxicity };

    [Fact]
    public void Armour_lowersKillChance_asDefensiveMass()
    {
        double bare = Combat.KillProbability(5.0, 5.0, Defender(), Combatant);
        double armoured = Combat.KillProbability(5.0, 5.0, Defender(armour: 1.0), Combatant);

        Assert.Equal(0.5, bare, precision: 10);
        Assert.True(armoured < bare); // armour adds defensive mass to the ratio
    }

    [Fact]
    public void Evasion_dodgesAFractionOfTheKillChance_cappedByConfig()
    {
        double bare = Combat.KillProbability(5.0, 5.0, Defender(), Combatant);
        double dodgy = Combat.KillProbability(5.0, 5.0, Defender(evasion: 1.0), Combatant);

        // Maximal evasion removes MaxEvasion of the landed chance.
        Assert.Equal(bare * (1.0 - Combatant.MaxEvasion), dodgy, precision: 10);
    }

    [Fact]
    public void ToxinContactDamage_scalesWithToxicity()
    {
        Assert.Equal(0.0, Combat.ToxinContactDamage(Defender(), Combatant));
        Assert.Equal(Combatant.ToxinContactDamage, Combat.ToxinContactDamage(Defender(toxicity: 1.0), Combatant), precision: 10);
        Assert.Equal(Combatant.ToxinContactDamage / 2.0, Combat.ToxinContactDamage(Defender(toxicity: 0.5), Combatant), precision: 10);
    }

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
