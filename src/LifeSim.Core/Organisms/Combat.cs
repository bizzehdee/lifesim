using LifeSim.Core.Configuration;

namespace LifeSim.Core.Organisms;

/// <summary>Predatory combat math: pure so it's testable without a live world.</summary>
public static class Combat
{
    /// <summary>
    /// P(kill) = Size_attacker / (Size_attacker + Size_victim). Bounded and symmetric — stays in
    /// (0, 1) and avoids the blow-ups of a raw size ratio.
    /// </summary>
    public static double KillProbability(double attackerSize, double victimSize) =>
        attackerSize / (attackerSize + victimSize);

    /// <summary>
    /// The chance an attacker of <paramref name="attackerMass"/> kills a defender, accounting for the
    /// defender's evolved defences: <c>armour</c> adds absolute toughness to the mass ratio (defence
    /// only — it never helped the defender attack), then <c>evasion</c> dodges a fraction of the
    /// remaining chance. Attacker mass is its plain combat mass (offence is unarmoured).
    /// </summary>
    public static double KillProbability(double attackerMass, double defenderMass, Genome defender, MovementCombatConfig config)
    {
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(config);
        double effectiveDefence = defenderMass + (Math.Clamp(defender.Armour, 0.0, 1.0) * config.ArmourScale);
        double landed = KillProbability(attackerMass, effectiveDefence);
        return landed * (1.0 - (Math.Clamp(defender.Evasion, 0.0, 1.0) * config.MaxEvasion));
    }

    /// <summary>Energy an attacker loses on contact with a defender, proportional to the defender's <c>toxicity</c>.</summary>
    public static double ToxinContactDamage(Genome defender, MovementCombatConfig config)
    {
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(config);
        return Math.Clamp(defender.Toxicity, 0.0, 1.0) * config.ToxinContactDamage;
    }
}
