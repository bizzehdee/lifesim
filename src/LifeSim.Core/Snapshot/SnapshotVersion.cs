namespace LifeSim.Core.Snapshot;

/// <summary>Thrown when a snapshot fails validation or version gating on import.</summary>
public sealed class SnapshotValidationException : Exception
{
    public SnapshotValidationException(string message) : base(message) { }
}

/// <summary>
/// Minimal semver (major.minor[.patch]) used for the import gate:
/// hard-reject on major mismatch; a different minor is allowed.
/// </summary>
public readonly record struct SnapshotVersion(int Major, int Minor)
{
    public static SnapshotVersion Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string[] parts = value.Split('.');
        if (parts.Length < 2
            || !int.TryParse(parts[0], out int major)
            || !int.TryParse(parts[1], out int minor))
        {
            throw new SnapshotValidationException($"Malformed version string '{value}' (expected major.minor).");
        }

        return new SnapshotVersion(major, minor);
    }

    /// <summary>Reject on major mismatch. <paramref name="expected"/> is the running engine's version.</summary>
    public static void GateOrThrow(string kind, string actual, string expected)
    {
        SnapshotVersion a = Parse(actual);
        SnapshotVersion e = Parse(expected);
        if (a.Major != e.Major)
        {
            throw new SnapshotValidationException(
                $"Incompatible {kind} version {actual}: engine supports {e.Major}.x (got major {a.Major}).");
        }
    }
}
