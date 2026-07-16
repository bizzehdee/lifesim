using LifeSim.Core.Configuration;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Neat;

/// <summary>
/// Converts the immediate consequence of an organism's chosen action into a bounded learning signal.
/// Unavoidable basal, thermal, aging, and crowding costs are deliberately excluded: otherwise every
/// action is punished merely for being alive. Reproduction receives direct fitness credit instead of
/// being misread as an energy loss.
/// </summary>
public static class LearningReward
{
    public static double Calculate(
        OrganismAction action,
        ActionResult result,
        double actionEnergyDelta,
        double locomotionCost,
        LearningConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        double bound = Math.Max(0.0, config.RewardClamp);
        double failurePenalty = Math.Clamp(config.FailedActionPenalty, 0.0, bound);

        if (action == OrganismAction.Reproduce)
        {
            return result == ActionResult.Success
                ? Math.Clamp(config.ReproductionReward, -bound, bound)
                : -failurePenalty;
        }

        double scale = Math.Max(double.Epsilon, Math.Abs(config.EnergyRewardScale));
        double reward = (actionEnergyDelta - Math.Max(0.0, locomotionCost)) / scale;

        if (action != OrganismAction.Idle)
        {
            reward += result switch
            {
                ActionResult.Success or ActionResult.Killed => config.SuccessfulActionReward,
                ActionResult.Blocked or ActionResult.Failed or ActionResult.NoOp => -failurePenalty,
                _ => 0.0,
            };
        }

        return Math.Clamp(reward, -bound, bound);
    }
}
