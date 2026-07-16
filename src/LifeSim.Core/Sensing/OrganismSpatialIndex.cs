using LifeSim.Core.Organisms;
using LifeSim.Core.Simulation;

namespace LifeSim.Core.Sensing;

/// <summary>
/// Tile-backed organism proximity index. A radius query examines only the bounded square around the
/// observer, rather than scanning the population; the circular distance check removes corner tiles.
/// The simulation attaches this view to its authoritative occupancy grid once per sensing phase.
/// </summary>
public sealed class OrganismSpatialIndex
{
    private readonly IReadOnlyDictionary<long, Organism> _organisms;
    private readonly OccupancyGrid _occupancy;

    private OrganismSpatialIndex(OccupancyGrid occupancy, IReadOnlyDictionary<long, Organism> organisms)
    {
        _occupancy = occupancy;
        _organisms = organisms;
    }

    /// <summary>Builds a standalone index, primarily for tools and isolated sensory evaluation.</summary>
    public static OrganismSpatialIndex Create(
        int width,
        int height,
        IReadOnlyDictionary<long, Organism> organisms)
    {
        ArgumentNullException.ThrowIfNull(organisms);
        var occupancy = new OccupancyGrid(width, height);
        foreach (Organism organism in organisms.Values)
        {
            if (organism.X < 0 || organism.X >= width || organism.Y < 0 || organism.Y >= height)
            {
                throw new ArgumentOutOfRangeException(nameof(organisms), "An organism lies outside the indexed world.");
            }

            if (occupancy.IsOccupied(organism.X, organism.Y))
            {
                throw new ArgumentException("Multiple organisms occupy the same indexed tile.", nameof(organisms));
            }

            occupancy.Set(organism.X, organism.Y, organism.Id);
        }

        return new OrganismSpatialIndex(occupancy, organisms);
    }

    internal static OrganismSpatialIndex Attach(
        OccupancyGrid occupancy,
        IReadOnlyDictionary<long, Organism> organisms) => new(occupancy, organisms);

    /// <summary>Enumerates organisms in Euclidean range, in stable tile (y, x) order.</summary>
    public IEnumerable<Organism> WithinRadius(int centerX, int centerY, int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        long radiusSquared = (long)radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if ((((long)dx * dx) + ((long)dy * dy)) > radiusSquared
                    || !_occupancy.TryGet(centerX + dx, centerY + dy, out long id))
                {
                    continue;
                }

                // Reserved birth tiles can exist in occupancy before their organism is committed.
                if (_organisms.TryGetValue(id, out Organism? organism))
                {
                    yield return organism;
                }
            }
        }
    }
}
