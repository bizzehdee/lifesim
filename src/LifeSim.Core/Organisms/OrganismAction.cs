namespace LifeSim.Core.Organisms;

/// <summary>
/// The 15 action outputs the brain selects between (lifesim.md §4, §17, §20). Named
/// <c>OrganismAction</c> rather than <c>Action</c> to avoid colliding with <see cref="System.Action"/>,
/// which is in scope everywhere via implicit usings. The underlying values are the output-node index
/// order (matches <see cref="Neat.NeatTopology.OutputNodeIds"/> position-for-position), so new actions
/// are appended, never inserted.
/// </summary>
public enum OrganismAction
{
    MoveNorth = 0,
    MoveSouth = 1,
    MoveEast = 2,
    MoveWest = 3,
    HarvestSelf = 4,
    HarvestNorth = 5,
    HarvestSouth = 6,
    HarvestEast = 7,
    HarvestWest = 8,
    Idle = 9,
    Reproduce = 10,

    // Cooperation (lifesim.md §20): donate energy to the adjacent organism in that direction.
    ShareNorth = 11,
    ShareSouth = 12,
    ShareEast = 13,
    ShareWest = 14,
}
