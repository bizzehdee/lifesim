namespace LifeSim.Core.Simulation;

/// <summary>
/// Maps each world tile to the id of the organism standing on it, backed by a dense flat array
/// (row-major, width × height) rather than a hashed (x, y) dictionary. Every tick performs thousands
/// of neighbour lookups — sensing, local density, movement, combat, reproduction — so O(1) array
/// indexing without tuple hashing is a measurable win at high population.
///
/// Empty tiles read as the array's <c>0</c> default (an id is stored as <c>id + 1</c>), so a freshly
/// allocated grid is already all-empty with no initialization pass. Reads are bounds-safe: an off-grid
/// tile reads as empty, matching the dictionary-miss semantics the density scan relies on. Writes
/// assume in-bounds coordinates, which every caller validates before writing.
///
/// The parallel tick phases only read occupancy (writes happen in the serial intent/death/birth
/// phases), and concurrent reads of a shared array are safe, so this preserves thread-count determinism.
/// </summary>
internal sealed class OccupancyGrid
{
    private readonly long[] _cells;
    private readonly int _width;
    private readonly int _height;

    public OccupancyGrid(int width, int height)
    {
        _width = width;
        _height = height;
        _cells = new long[width * height];
    }

    /// <summary>True if a live-or-reserved organism holds this tile; off-grid tiles read as empty.</summary>
    public bool IsOccupied(int x, int y) =>
        x >= 0 && x < _width && y >= 0 && y < _height && _cells[(y * _width) + x] != 0;

    /// <summary>Gets the id holding this tile; false (and off-grid) reads as empty.</summary>
    public bool TryGet(int x, int y, out long id)
    {
        if (x >= 0 && x < _width && y >= 0 && y < _height)
        {
            long slot = _cells[(y * _width) + x];
            if (slot != 0)
            {
                id = slot - 1;
                return true;
            }
        }

        id = 0;
        return false;
    }

    /// <summary>Marks an in-bounds tile as held by <paramref name="id"/>.</summary>
    public void Set(int x, int y, long id) => _cells[(y * _width) + x] = id + 1;

    /// <summary>Frees an in-bounds tile.</summary>
    public void Clear(int x, int y) => _cells[(y * _width) + x] = 0;
}
