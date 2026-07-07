using System.Globalization;
using LifeSim.Core.Snapshot;

namespace LifeSim.Core.Editing;

/// <summary>
/// Applies explicit UI interventions to a snapshot. Each edit returns a new
/// snapshot with the field changed <em>and</em> an <see cref="EditLogEntry"/> appended, so the
/// change is never silent. The result carries full organism/PRNG/config state, so loading it via
/// <see cref="Simulation.SimulationWorld.FromSnapshot"/> is a new, replayable deterministic starting
/// point.
/// </summary>
public static class SnapshotEditor
{
    /// <summary>Sets one organism's energy, recording the intervention in the edit log.</summary>
    public static WorldSnapshot SetOrganismEnergy(WorldSnapshot snapshot, long organismId, double newEnergy, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        OrganismSnapshot? target = snapshot.Organisms.FirstOrDefault(o => o.OrganismId == organismId)
            ?? throw new ArgumentException($"No organism {organismId} in the snapshot.", nameof(organismId));

        double previous = target.Energy;
        List<OrganismSnapshot> organisms = snapshot.Organisms
            .Select(o => o.OrganismId == organismId ? o with { Energy = newEnergy } : o)
            .ToList();

        var entry = new EditLogEntry
        {
            Tick = snapshot.Tick,
            Target = $"organism:{organismId}",
            Field = "energy",
            PreviousValue = previous.ToString(CultureInfo.InvariantCulture),
            NewValue = newEnergy.ToString(CultureInfo.InvariantCulture),
            Reason = reason,
        };

        return snapshot with { Organisms = organisms, EditLog = [.. snapshot.EditLog, entry] };
    }
}
