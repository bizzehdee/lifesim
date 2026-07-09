using System.Text.Json.Nodes;
using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class SnapshotSerializerTests
{
    private static WorldSnapshot SampleSnapshot()
    {
        var brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(7));
        var organism = new Organism(1, Genome.MidRange(new TraitBounds()), "Silent-Amber-Vole", 50.0, 3, 4, brain);

        return new WorldSnapshot
        {
            Tick = 123,
            World = new WorldState { Seed = 42, Width = 256, Height = 256 },
            Configuration = SimulationConfig.Default,
            PrngStreams = PrngStreams.FromSeed(42).CaptureState(),
            EvolutionBookkeeping = new EvolutionBookkeeping { NextInnovationId = 5, NextOrganismId = 10 },
            Organisms = [OrganismSnapshot.From(organism)],
            Metrics = new SimulationMetrics { Population = 1, Extinct = false },
        };
    }

    [Fact]
    public void SaveThenLoad_roundTripsLosslessly()
    {
        WorldSnapshot original = SampleSnapshot();

        string json = SnapshotSerializer.Save(original);
        WorldSnapshot loaded = SnapshotSerializer.Load(json);

        Assert.Equal(original.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(original.ConfigVersion, loaded.ConfigVersion);
        Assert.Equal(original.Tick, loaded.Tick);
        Assert.Equal(original.World, loaded.World);
        Assert.Equal(original.EvolutionBookkeeping, loaded.EvolutionBookkeeping);

        // Configuration contains collection members (e.g. founding composition) whose record equality is
        // by-reference, so assert lossless config round-trip via re-serialization idempotence instead —
        // a strictly stronger guarantee that also catches any serialization drift.
        Assert.Equal(json, SnapshotSerializer.Save(loaded));

        // PRNG stream state must round-trip exactly.
        Assert.Equal(original.PrngStreams.Count, loaded.PrngStreams.Count);
        foreach ((string name, ulong[] words) in original.PrngStreams)
        {
            Assert.True(loaded.PrngStreams[name].SequenceEqual(words));
        }

        Assert.Single(loaded.Organisms);
        Assert.Equal(original.Organisms[0], loaded.Organisms[0]);
        Assert.Equal(original.Metrics, loaded.Metrics);
    }

    [Fact]
    public void GroundEnergyAndDebugTerrain_roundTrip()
    {
        WorldSnapshot original = SampleSnapshot() with
        {
            GroundEnergy = [new GroundEnergyEntry(3, 4, 12.5), new GroundEnergyEntry(-1, 0, 0.0)],
            DebugTerrain = [new DebugTileEntry(0, 0, Biome.Grassland, 0.1, 0.2)],
        };

        WorldSnapshot loaded = SnapshotSerializer.Load(SnapshotSerializer.Save(original));

        Assert.Equal(original.GroundEnergy, loaded.GroundEnergy);
        Assert.Equal(original.DebugTerrain, loaded.DebugTerrain);
    }

    [Fact]
    public void Lineages_roundTrip_includingClosedRecords()
    {
        var birthTraits = Genome.MidRange(new TraitBounds());
        var deathTraits = birthTraits with { Size = birthTraits.Size + 1.0 };

        WorldSnapshot original = SampleSnapshot() with
        {
            Lineages =
            [
                new LineageSnapshot
                {
                    OrganismId = 1, ParentId = null, LineageId = 1, BirthTick = 0,
                    GenerationDepth = 0, BirthTraits = GenomeSnapshot.From(birthTraits),
                },
                new LineageSnapshot
                {
                    OrganismId = 2, ParentId = 1, LineageId = 1, BirthTick = 5, DeathTick = 20,
                    GenerationDepth = 1, BirthTraits = GenomeSnapshot.From(birthTraits),
                    DeathTraits = GenomeSnapshot.From(deathTraits),
                },
            ],
        };

        WorldSnapshot loaded = SnapshotSerializer.Load(SnapshotSerializer.Save(original));

        Assert.Equal(original.Lineages, loaded.Lineages);
    }

    [Fact]
    public void DebugTerrain_isOmittedWhenNull()
    {
        WorldSnapshot original = SampleSnapshot();
        Assert.Null(original.DebugTerrain);

        string json = SnapshotSerializer.Save(original);
        Assert.DoesNotContain("debug_terrain", json);

        WorldSnapshot loaded = SnapshotSerializer.Load(json);
        Assert.Null(loaded.DebugTerrain);
    }

    [Fact]
    public void Load_rejectsMalformedJson()
    {
        Assert.Throws<SnapshotValidationException>(() => SnapshotSerializer.Load("{ this is not json"));
    }

    [Fact]
    public void Load_rejectsIncompatibleMajorVersion()
    {
        JsonObject obj = JsonNode.Parse(SnapshotSerializer.Save(SampleSnapshot()))!.AsObject();
        obj["schema_version"] = "99.0"; // a major far beyond anything the engine supports

        var ex = Assert.Throws<SnapshotValidationException>(() => SnapshotSerializer.Load(obj.ToJsonString()));
        Assert.Contains("schema", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_rejectsPre2_0Snapshots_whoseBrainsHaveTheOldInputWidth()
    {
        // schema 2.0 widened the sensory input vector (19 → 26); a schema-1.x snapshot's brains are
        // structurally incompatible, so it must be rejected rather than loaded and run corrupt.
        JsonObject obj = JsonNode.Parse(SnapshotSerializer.Save(SampleSnapshot()))!.AsObject();
        obj["schema_version"] = "1.2";

        var ex = Assert.Throws<SnapshotValidationException>(() => SnapshotSerializer.Load(obj.ToJsonString()));
        Assert.Contains("schema", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_rejectsMissingRequiredBlock()
    {
        JsonObject obj = JsonNode.Parse(SnapshotSerializer.Save(SampleSnapshot()))!.AsObject();
        obj.Remove("organisms");

        Assert.Throws<SnapshotValidationException>(() => SnapshotSerializer.Load(obj.ToJsonString()));
    }

    [Fact]
    public void Load_rejectsMalformedVersionString()
    {
        JsonObject obj = JsonNode.Parse(SnapshotSerializer.Save(SampleSnapshot()))!.AsObject();
        obj["config_version"] = "not-a-version";

        Assert.Throws<SnapshotValidationException>(() => SnapshotSerializer.Load(obj.ToJsonString()));
    }
}
