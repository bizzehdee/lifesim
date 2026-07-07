using System.Reflection;
using System.Text.Json;
using Json.Schema;

namespace LifeSim.Core.Snapshot;

/// <summary>
/// Loads the embedded JSON Schema and validates snapshot JSON against it before import
///. The schema is the envelope contract; deeper blocks are enforced by later phases.
/// </summary>
internal static class SnapshotSchema
{
    private const string ResourceName = "LifeSim.Core.Snapshot.schema.json";

    private static readonly JsonSchema Schema = LoadSchema();

    /// <summary>Validate a parsed snapshot document; throws <see cref="SnapshotValidationException"/> if invalid.</summary>
    public static void Validate(JsonElement snapshot)
    {
        EvaluationResults results = Schema.Evaluate(snapshot, new EvaluationOptions
        {
            OutputFormat = OutputFormat.Flag,
        });

        if (!results.IsValid)
        {
            throw new SnapshotValidationException("Snapshot does not conform to the LifeSim snapshot schema.");
        }
    }

    private static JsonSchema LoadSchema()
    {
        Assembly assembly = typeof(SnapshotSchema).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded schema resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
