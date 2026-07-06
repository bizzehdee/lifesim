namespace LifeSim.Core.Organisms;

/// <summary>
/// Allocates stable, never-reused organism ids from the <c>evolution_bookkeeping.next_organism_id</c>
/// counter (lifesim.md §9, §12). Rehydrate from a snapshot's current value on resume so ids never
/// repeat across a save/reload.
/// </summary>
public sealed class OrganismIdAllocator
{
    public OrganismIdAllocator(long nextId) => NextId = nextId;

    public long NextId { get; private set; }

    public long Allocate()
    {
        long id = NextId;
        NextId++;
        return id;
    }
}
