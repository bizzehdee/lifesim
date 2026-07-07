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
}
