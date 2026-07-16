using System.Text.Json;
using System.Text.Json.Serialization;
using LifeSim.Core.Events;
using LifeSim.Core.Organisms;
using LifeSim.Core.World;

namespace LifeSim.Core.Snapshot;

/// <summary>Compact, AOT-safe frame serialization; frames are ephemeral and do not use checkpoint schema validation.</summary>
public static class WorldFrameSerializer
{
    public static string Save(WorldFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return JsonSerializer.Serialize(frame, WorldFrameJsonContext.Default.WorldFrame);
    }

    public static WorldFrame Load(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            return JsonSerializer.Deserialize(json, WorldFrameJsonContext.Default.WorldFrame)
                ?? throw new SnapshotValidationException("Frame deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new SnapshotValidationException($"Malformed frame JSON: {ex.Message}");
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [
        typeof(JsonStringEnumConverter<Biome>),
        typeof(JsonStringEnumConverter<OrganismAction>),
        typeof(JsonStringEnumConverter<ActionResult>),
        typeof(JsonStringEnumConverter<EventType>)])]
[JsonSerializable(typeof(WorldFrame))]
[JsonSerializable(typeof(OrganismFrame))]
internal sealed partial class WorldFrameJsonContext : JsonSerializerContext
{
}
