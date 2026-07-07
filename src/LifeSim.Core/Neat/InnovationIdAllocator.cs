namespace LifeSim.Core.Neat;

/// <summary>
/// Allocates globally-unique, never-reused innovation ids for NEAT structural mutations from the
/// <c>evolution_bookkeeping.next_innovation_id</c> counter (lifesim.md §4, §9, §12). Node ids and
/// connection innovation ids share this single counter (a node's <see cref="NodeGene.Id"/> is an
/// innovation number in this model). It is advanced only in the Birth Commit phase, in ascending
/// offspring-id order, and rehydrated from the snapshot on resume so ids never repeat.
/// </summary>
public sealed class InnovationIdAllocator
{
    public InnovationIdAllocator(long nextId) => NextId = nextId;

    public long NextId { get; private set; }

    public long Allocate()
    {
        long id = NextId;
        NextId++;
        return id;
    }
}
