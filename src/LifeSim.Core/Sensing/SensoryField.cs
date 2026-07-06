namespace LifeSim.Core.Sensing;

/// <summary>
/// Named indices into the fixed sensory input vector (lifesim.md §13). The enum's underlying
/// values are the exact input-node index order fed to the brain — matches
/// <see cref="Neat.NeatTopology.InputNodeIds"/> position-for-position.
/// </summary>
public enum SensoryField
{
    Energy = 0,
    Age = 1,
    TileTemperature = 2,
    BiomeFriction = 3,
    RichestTileDistance = 4,
    RichestTileDirectionX = 5,
    RichestTileDirectionY = 6,
    ClosestOrganismDistance = 7,
    ClosestOrganismDirectionX = 8,
    ClosestOrganismDirectionY = 9,
    ClosestOrganismSizeDelta = 10,
    NearbySmallerCount = 11,
    NearbyLargerCount = 12,
    LocalDensity = 13,
    LastActionResult = 14,
    ReproductiveReadiness = 15,
    GlobalStressLevel = 16,
}
