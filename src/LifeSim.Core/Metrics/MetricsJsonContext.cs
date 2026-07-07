using System.Text.Json.Serialization;
using LifeSim.Core.Events;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Metrics;

/// <summary>
/// Compact (single-line) source-generation context for the metrics stream — distinct from the
/// indented snapshot context so each <see cref="MetricsSample"/> serializes to exactly one line of
/// newline-delimited JSON. Source generation keeps it trimming/AOT-friendly for
/// the WASM target.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [
        typeof(JsonStringEnumConverter<Biome>),
        typeof(JsonStringEnumConverter<EventType>)])]
[JsonSerializable(typeof(MetricsSample))]
internal sealed partial class MetricsJsonContext : JsonSerializerContext
{
}
