namespace LifeSim.Core.Organisms;

/// <summary>The outcome of an organism's last action (lifesim.md §12, §13's "last action result").</summary>
public enum ActionResult
{
    /// <summary>No action has been recorded yet (before an organism's first decision).</summary>
    None,

    /// <summary>The action achieved its purpose: moved, grazed (even zero energy), killed prey, or gave birth.</summary>
    Success,

    /// <summary>A Move that was blocked immediately (off-grid or occupied on the very first step).</summary>
    Blocked,

    /// <summary>A Harvest combat roll that failed (survived but paid the retaliation penalty), or a Reproduce attempt that failed a validity gate.</summary>
    Failed,

    /// <summary>A Harvest aimed off-grid: no target tile exists at all.</summary>
    NoOp,
}
