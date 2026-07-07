namespace LifeSim.Core.Snapshot;

/// <summary>
/// One explicit UI intervention. Every edit made in the Avalonia app appends an
/// entry so a manually-changed world can never be mistaken for an untouched deterministic run.
/// Values are stored as strings so any edited field (organism or world) serializes uniformly.
/// </summary>
public sealed record EditLogEntry
{
    /// <summary>The tick at which the intervention was made.</summary>
    public long Tick { get; init; }

    /// <summary>The edited entity or world field, e.g. <c>organism:12</c> or <c>world</c>.</summary>
    public string Target { get; init; } = "";

    /// <summary>The field name within the target, e.g. <c>energy</c>.</summary>
    public string Field { get; init; } = "";

    public string PreviousValue { get; init; } = "";

    public string NewValue { get; init; } = "";

    /// <summary>Optional user-facing reason/label for the intervention.</summary>
    public string? Reason { get; init; }
}
