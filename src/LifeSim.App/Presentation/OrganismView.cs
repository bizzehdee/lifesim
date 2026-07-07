using Avalonia.Media;

namespace LifeSim.App.Presentation;

/// <summary>
/// A render-ready organism: everything the canvas needs to draw one marker, precomputed from that
/// organism's snapshot record (lifesim.md §18). Position is in world-tile coordinates; radius is a
/// fraction of a tile (the canvas scales both by the current zoom).
/// </summary>
public sealed record OrganismView
{
    public required long Id { get; init; }
    public required long LineageId { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }

    /// <summary>Marker radius in tile units (∝ Size trait).</summary>
    public required double Radius { get; init; }

    /// <summary>Fill = the active colour mode.</summary>
    public required Color Fill { get; init; }

    /// <summary>Outline/halo = last action (always on, independent of the colour mode).</summary>
    public required Color Outline { get; init; }

    public bool ReproductiveReady { get; init; }
    public bool Stressed { get; init; }

    /// <summary>Last action killed prey this tick — the canvas flashes the tile (lifesim.md §18).</summary>
    public bool JustKilled { get; init; }
}
