using System.Text.Json;

namespace LifeSim.Core.Snapshot;

/// <summary>
/// Reads and writes snapshots. Import is gated (lifesim.md §12): the file is parsed, validated
/// against the embedded JSON Schema, version-gated (hard-reject on major mismatch of schema or
/// config version), and only then deserialized into the typed model.
/// </summary>
public static class SnapshotSerializer
{
    /// <summary>Serialize a snapshot to JSON.</summary>
    public static string Save(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, SnapshotJsonContext.Default.WorldSnapshot);
    }

    /// <summary>
    /// Validate and deserialize a snapshot from JSON. Throws <see cref="SnapshotValidationException"/>
    /// for malformed JSON, schema violations, or an incompatible major version.
    /// </summary>
    public static WorldSnapshot Load(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new SnapshotValidationException($"Malformed snapshot JSON: {ex.Message}");
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            // 1. Structural validation against the embedded schema.
            SnapshotSchema.Validate(root);

            // 2. Version gating (schema guaranteed the fields exist and are well-formed).
            string schemaVersion = root.GetProperty("schema_version").GetString()!;
            string configVersion = root.GetProperty("config_version").GetString()!;
            SnapshotVersion.GateOrThrow("schema", schemaVersion, BuildInfo.SchemaVersion);
            SnapshotVersion.GateOrThrow("config", configVersion, BuildInfo.ConfigVersion);
        }

        // 3. Typed deserialization.
        WorldSnapshot? snapshot = JsonSerializer.Deserialize(json, SnapshotJsonContext.Default.WorldSnapshot);
        return snapshot ?? throw new SnapshotValidationException("Snapshot deserialized to null.");
    }
}
