using LifeSim.Core.Configuration;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;

namespace LifeSim.Core.Tests;

public class LineageEntryTests
{
    [Fact]
    public void SecondParentId_defaultsToNull_forAsexualEntries()
    {
        Genome genome = Genome.MidRange(new TraitBounds());
        var entry = new LineageEntry(2, parentId: 1, lineageId: 1, birthTick: 5, generationDepth: 1, birthTraits: genome);

        Assert.Null(entry.SecondParentId);
    }

    [Fact]
    public void SecondParentId_roundTripsThroughASnapshot()
    {
        Genome genome = Genome.MidRange(new TraitBounds());
        var sexualOffspring = new LineageEntry(
            3, parentId: 1, lineageId: 1, birthTick: 7, generationDepth: 2, birthTraits: genome,
            foundingType: "Generic", secondParentId: 2);

        LineageEntry restored = LineageSnapshot.From(sexualOffspring).ToEntry();

        Assert.Equal(1, restored.ParentId);
        Assert.Equal(2, restored.SecondParentId);
        Assert.Equal(1, restored.LineageId); // still follows the initiator
    }

    [Fact]
    public void Constructor_startsAlive_withNoDeathRecorded()
    {
        Genome genome = Genome.MidRange(new TraitBounds());
        var entry = new LineageEntry(1, parentId: null, lineageId: 1, birthTick: 0, generationDepth: 0, birthTraits: genome);

        Assert.Null(entry.DeathTick);
        Assert.Null(entry.DeathTraits);
        Assert.Null(entry.ParentId);
        Assert.Equal(1, entry.LineageId);
    }

    [Fact]
    public void RecordDeath_setsDeathTickAndTraits()
    {
        Genome birthTraits = Genome.MidRange(new TraitBounds());
        Genome deathTraits = birthTraits with { Size = birthTraits.Size + 2.0 };
        var entry = new LineageEntry(1, parentId: null, lineageId: 1, birthTick: 0, generationDepth: 0, birthTraits: birthTraits);

        entry.RecordDeath(42, deathTraits);

        Assert.Equal(42, entry.DeathTick);
        Assert.Equal(deathTraits, entry.DeathTraits);
    }

    [Fact]
    public void OffspringEntry_inheritsFoundersLineageId_notItsOwnId()
    {
        Genome genome = Genome.MidRange(new TraitBounds());
        var founder = new LineageEntry(1, parentId: null, lineageId: 1, birthTick: 0, generationDepth: 0, birthTraits: genome);
        var offspring = new LineageEntry(2, parentId: 1, lineageId: founder.LineageId, birthTick: 5, generationDepth: 1, birthTraits: genome);

        Assert.Equal(founder.LineageId, offspring.LineageId);
        Assert.Equal(1, offspring.ParentId);
        Assert.Equal(1, offspring.GenerationDepth);
    }
}
