using LifeSim.Core.Configuration;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.ViewModels;

/// <summary>
/// The stats available for any organism from the all-time lineage records (lifesim.md §8, §14) —
/// used for the ranking detail of a <em>dead</em> organism, which has no live brain/energy but does
/// have birth (and death) trait summaries. Living organisms use the full
/// <see cref="OrganismInspectorViewModel"/> instead.
/// </summary>
public sealed class LineageDetailViewModel : ViewModelBase
{
    public long OrganismId { get; private init; }
    public long LineageId { get; private init; }
    public long? ParentId { get; private init; }
    public int GenerationDepth { get; private init; }
    public long BirthTick { get; private init; }
    public long? DeathTick { get; private init; }
    public long Lifespan { get; private init; }
    public long ChildCount { get; private init; }
    public bool IsAlive { get; private init; }
    public IReadOnlyList<TraitReading> BirthTraits { get; private init; } = [];
    public IReadOnlyList<TraitReading> DeathTraits { get; private init; } = [];
    public bool HasDeathTraits => DeathTraits.Count > 0;

    public static LineageDetailViewModel? Create(WorldSnapshot snapshot, long organismId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        LineageSnapshot? lineage = snapshot.Lineages.FirstOrDefault(l => l.OrganismId == organismId);
        if (lineage is null)
        {
            return null;
        }

        TraitBounds bounds = snapshot.Configuration.TraitBounds;
        bool alive = snapshot.Organisms.Any(o => o.OrganismId == organismId);
        long end = lineage.DeathTick ?? snapshot.Tick;

        return new LineageDetailViewModel
        {
            OrganismId = lineage.OrganismId,
            LineageId = lineage.LineageId,
            ParentId = lineage.ParentId,
            GenerationDepth = lineage.GenerationDepth,
            BirthTick = lineage.BirthTick,
            DeathTick = lineage.DeathTick,
            Lifespan = Math.Max(0, end - lineage.BirthTick),
            ChildCount = snapshot.Lineages.Count(l => l.ParentId == organismId),
            IsAlive = alive,
            BirthTraits = ToReadings(lineage.BirthTraits, bounds),
            DeathTraits = lineage.DeathTraits is { } death ? ToReadings(death, bounds) : [],
        };
    }

    private static IReadOnlyList<TraitReading> ToReadings(GenomeSnapshot g, TraitBounds bounds) =>
    [
        new TraitReading("Size", g.Size, bounds.Size.Min, bounds.Size.Max),
        new TraitReading("Speed Capacity", g.SpeedCapacity, bounds.SpeedCapacity.Min, bounds.SpeedCapacity.Max),
        new TraitReading("Thermal Centre", g.ThermalCenter, bounds.ThermalCenter.Min, bounds.ThermalCenter.Max),
        new TraitReading("Thermal Width", g.ThermalWidth, bounds.ThermalWidth.Min, bounds.ThermalWidth.Max),
        new TraitReading("Env Radius", g.EnvRadius, bounds.EnvRadius.Min, bounds.EnvRadius.Max),
        new TraitReading("Org Radius", g.OrgRadius, bounds.OrgRadius.Min, bounds.OrgRadius.Max),
        new TraitReading("Sensory Acuity", g.SensoryAcuity, bounds.SensoryAcuity.Min, bounds.SensoryAcuity.Max),
        new TraitReading("Generosity", g.ShareFraction, bounds.ShareFraction.Min, bounds.ShareFraction.Max),
    ];
}
