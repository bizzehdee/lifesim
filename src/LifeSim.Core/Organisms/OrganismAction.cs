namespace LifeSim.Core.Organisms;

/// <summary>
/// The 11 action outputs the brain selects between (lifesim.md §4, §17). Named
/// <c>OrganismAction</c> rather than <c>Action</c> to avoid colliding with <see cref="System.Action"/>,
/// which is in scope everywhere via implicit usings.
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
}
