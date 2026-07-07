namespace LifeSim.Core.Organisms;

/// <summary>
/// Ancestry record for one organism, opened at birth and closed at death.
/// Unlike the live organism index, lineage records are never removed — they accumulate for the
/// whole run so lineage trees stay reconstructable after death.
/// </summary>
public sealed class LineageEntry
{
    public long OrganismId { get; }

    /// <summary>Null for genesis organisms.</summary>
    public long? ParentId { get; }

    /// <summary>The founding ancestor's <see cref="OrganismId"/> — genesis organisms found their own lineage; offspring inherit it unchanged (asexual reproduction).</summary>
    public long LineageId { get; }

    public long BirthTick { get; }

    public int GenerationDepth { get; }

    public Genome BirthTraits { get; }

    public long? DeathTick { get; private set; }

    public Genome? DeathTraits { get; private set; }

    public LineageEntry(long organismId, long? parentId, long lineageId, long birthTick, int generationDepth, Genome birthTraits)
    {
        ArgumentNullException.ThrowIfNull(birthTraits);
        OrganismId = organismId;
        ParentId = parentId;
        LineageId = lineageId;
        BirthTick = birthTick;
        GenerationDepth = generationDepth;
        BirthTraits = birthTraits;
    }

    public void RecordDeath(long deathTick, Genome deathTraits)
    {
        ArgumentNullException.ThrowIfNull(deathTraits);
        DeathTick = deathTick;
        DeathTraits = deathTraits;
    }
}
