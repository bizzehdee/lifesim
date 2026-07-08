using System.Text.Json;
using System.Text.Json.Serialization;
using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.World;

namespace LifeSim.Core.Snapshot;

/// <summary>
/// System.Text.Json source-generation context for snapshots. Source generation keeps
/// (de)serialization trimming/AOT-friendly so the Core runs under the WASM target.
/// Snake_case naming matches the on-disk schema.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [
        typeof(JsonStringEnumConverter<Biome>),
        typeof(JsonStringEnumConverter<OrganismAction>),
        typeof(JsonStringEnumConverter<ActionResult>),
        typeof(JsonStringEnumConverter<NodeType>),
        typeof(JsonStringEnumConverter<EventType>)])]
[JsonSerializable(typeof(WorldSnapshot))]
[JsonSerializable(typeof(SimulationConfig))]
[JsonSerializable(typeof(BrainTypeSpec))]
[JsonSerializable(typeof(EnvironmentModifier))]
[JsonSerializable(typeof(GroundEnergyEntry))]
[JsonSerializable(typeof(DebugTileEntry))]
[JsonSerializable(typeof(OrganismSnapshot))]
[JsonSerializable(typeof(GenomeSnapshot))]
[JsonSerializable(typeof(SimulationMetrics))]
[JsonSerializable(typeof(TraitAverages))]
[JsonSerializable(typeof(TraitHistogram))]
[JsonSerializable(typeof(BiomePopulation))]
[JsonSerializable(typeof(LineageReproduction))]
[JsonSerializable(typeof(FoundingTypePopulation))]
[JsonSerializable(typeof(NeatGenome))]
[JsonSerializable(typeof(NodeGene))]
[JsonSerializable(typeof(ConnectionGene))]
[JsonSerializable(typeof(LineageSnapshot))]
[JsonSerializable(typeof(EditLogEntry))]
internal sealed partial class SnapshotJsonContext : JsonSerializerContext
{
}
