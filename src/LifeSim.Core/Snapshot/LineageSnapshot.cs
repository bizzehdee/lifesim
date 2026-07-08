using LifeSim.Core.Organisms;

namespace LifeSim.Core.Snapshot;

/// <summary>An ancestry record as stored in a snapshot.</summary>
public sealed record LineageSnapshot
{
    public long OrganismId { get; init; }
    public long? ParentId { get; init; }

    /// <summary>The sexual-reproduction co-parent, if any; null for genesis and asexual clones.</summary>
    public long? SecondParentId { get; init; }
    public long LineageId { get; init; }
    public long BirthTick { get; init; }
    public long? DeathTick { get; init; }
    public int GenerationDepth { get; init; }

    /// <summary>The lineage's seeded brain type; defaults to "Generic" so pre-typed snapshots still load.</summary>
    public string FoundingType { get; init; } = "Generic";
    public GenomeSnapshot BirthTraits { get; init; } = new();
    public GenomeSnapshot? DeathTraits { get; init; }

    public static LineageSnapshot From(LineageEntry entry) => new()
    {
        OrganismId = entry.OrganismId,
        ParentId = entry.ParentId,
        SecondParentId = entry.SecondParentId,
        LineageId = entry.LineageId,
        BirthTick = entry.BirthTick,
        DeathTick = entry.DeathTick,
        GenerationDepth = entry.GenerationDepth,
        FoundingType = entry.FoundingType,
        BirthTraits = GenomeSnapshot.From(entry.BirthTraits),
        DeathTraits = entry.DeathTraits is null ? null : GenomeSnapshot.From(entry.DeathTraits),
    };

    public LineageEntry ToEntry()
    {
        var entry = new LineageEntry(OrganismId, ParentId, LineageId, BirthTick, GenerationDepth, BirthTraits.ToGenome(), FoundingType, SecondParentId);
        if (DeathTick is not null && DeathTraits is not null)
        {
            entry.RecordDeath(DeathTick.Value, DeathTraits.ToGenome());
        }

        return entry;
    }
}
