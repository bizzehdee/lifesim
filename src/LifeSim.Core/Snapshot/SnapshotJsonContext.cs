using System.Text.Json;
using System.Text.Json.Serialization;
using LifeSim.Core.World;

namespace LifeSim.Core.Snapshot;

/// <summary>
/// System.Text.Json source-generation context for snapshots. Source generation keeps
/// (de)serialization trimming/AOT-friendly so the Core runs under the WASM target (lifesim.md §1).
/// Snake_case naming matches the on-disk schema (lifesim.md §12).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(JsonStringEnumConverter<Biome>)])]
[JsonSerializable(typeof(WorldSnapshot))]
[JsonSerializable(typeof(GroundEnergyEntry))]
[JsonSerializable(typeof(DebugTileEntry))]
internal sealed partial class SnapshotJsonContext : JsonSerializerContext
{
}
