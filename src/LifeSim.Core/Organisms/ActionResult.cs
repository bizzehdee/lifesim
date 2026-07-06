namespace LifeSim.Core.Organisms;

/// <summary>
/// The outcome of an organism's last action (lifesim.md §12, §13's "last action result"). Harvest
/// and Reproduce don't have real outcomes until Phase 7/8 land, so they always resolve to
/// <see cref="NoOp"/> for now regardless of whether they'd have been valid.
/// </summary>
public enum ActionResult
{
    /// <summary>No action has been recorded yet (before an organism's first decision).</summary>
    None,

    /// <summary>Idle, or a Move that actually travelled at least one tile.</summary>
    Success,

    /// <summary>A Move that was blocked immediately (off-grid or occupied on the very first step).</summary>
    Blocked,

    /// <summary>Harvest-*/Reproduce: mechanic not implemented yet (Phase 7/8).</summary>
    NoOp,
}
