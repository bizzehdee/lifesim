namespace LifeSim.Core.Snapshot;

/// <summary>
/// The world descriptor. Terrain is implicit — reconstructed from <see cref="Seed"/> and the
/// noise config, never stored.
/// </summary>
public sealed record WorldState
{
    public ulong Seed { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}
