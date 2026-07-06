using System.Text.Json;
using System.Text.Json.Nodes;
using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Snapshot;

namespace LifeSim.Core.Tests;

public class SnapshotSerializerTests
{
    private static WorldSnapshot SampleSnapshot() => new()
    {
        Tick = 123,
        World = new WorldState { Seed = 42, Width = 256, Height = 256 },
        Configuration = SimulationConfig.Default,
        PrngStreams = PrngStreams.FromSeed(42).CaptureState(),
        EvolutionBookkeeping = new EvolutionBookkeeping { NextInnovationId = 5, NextOrganismId = 10 },
        Organisms = [JsonDocument.Parse("""{"id":1}""").RootElement.Clone()],
    };

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
        Assert.Equal(original.Configuration, loaded.Configuration);
        Assert.Equal(original.EvolutionBookkeeping, loaded.EvolutionBookkeeping);

        // PRNG stream state must round-trip exactly (lifesim.md §9, §12).
        Assert.Equal(original.PrngStreams.Count, loaded.PrngStreams.Count);
        foreach ((string name, ulong[] words) in original.PrngStreams)
        {
            Assert.True(loaded.PrngStreams[name].SequenceEqual(words));
        }

        Assert.Single(loaded.Organisms);
        Assert.Equal(1, loaded.Organisms[0].GetProperty("id").GetInt32());
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
        obj["schema_version"] = "2.0";

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
