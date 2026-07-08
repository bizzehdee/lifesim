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

    /// <summary>
    /// The co-parent for an offspring produced by sexual reproduction, else null (genesis and asexual
    /// clones have a single parent). <see cref="LineageId"/> still follows <see cref="ParentId"/> (the
    /// initiator), so the lineage tree and descendant score are unchanged; this records the other parent
    /// for kin accounting and inspection.
    /// </summary>
    public long? SecondParentId { get; }

    /// <summary>The founding ancestor's <see cref="OrganismId"/> — genesis organisms found their own lineage; offspring inherit it unchanged (asexual reproduction).</summary>
    public long LineageId { get; }

    /// <summary>
    /// The brain "type" the founding ancestor was seeded with (e.g. "Selfish", "Generic"). A cosmetic,
    /// heritable label — offspring inherit it unchanged, so it stays constant down a lineage even as the
    /// brain itself evolves — used only to report which seeded types are winning. Never affects the sim.
    /// </summary>
    public string FoundingType { get; }

    public long BirthTick { get; }

    public int GenerationDepth { get; }

    public Genome BirthTraits { get; }

    public long? DeathTick { get; private set; }

    public Genome? DeathTraits { get; private set; }

    public LineageEntry(
        long organismId, long? parentId, long lineageId, long birthTick, int generationDepth, Genome birthTraits,
        string foundingType = "Generic", long? secondParentId = null)
    {
        ArgumentNullException.ThrowIfNull(birthTraits);
        ArgumentNullException.ThrowIfNull(foundingType);
        OrganismId = organismId;
        ParentId = parentId;
        LineageId = lineageId;
        BirthTick = birthTick;
        GenerationDepth = generationDepth;
        BirthTraits = birthTraits;
        FoundingType = foundingType;
        SecondParentId = secondParentId;
    }

    public void RecordDeath(long deathTick, Genome deathTraits)
    {
        ArgumentNullException.ThrowIfNull(deathTraits);
        DeathTick = deathTick;
        DeathTraits = deathTraits;
    }
}
