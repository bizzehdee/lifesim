namespace LifeSim.App.Presentation;

/// <summary>
/// What the organism <b>fill</b> channel encodes. The <b>outline</b> channel always
/// shows the last action independently, so both state and action stay readable at once.
/// </summary>
public enum ColourMode
{
    /// <summary>Fill mirrors the action palette — a pure action heatmap.</summary>
    Action,

    /// <summary>Red (near 0) → amber → green (near the energy cap).</summary>
    Energy,

    /// <summary>Green (grazing), red (predatory), grey (mixed/neither) — from the last action (see notes).</summary>
    DietTendency,

    /// <summary>How far the tile temperature sits outside the thermal envelope: blue (cold) ↔ neutral ↔ red (hot).</summary>
    StressFit,

    /// <summary>A stable hashed colour per <c>lineage_id</c>, so clonal clusters are visible.</summary>
    Lineage,

    /// <summary>Teal if the organism's last action was to share energy, else neutral — a cooperation readout.</summary>
    Cooperation,

    /// <summary>Brain "cognition" index (0–100): dim (simple brain) → bright (deep, well-wired brain).</summary>
    Intelligence,
}
