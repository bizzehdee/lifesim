using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;

namespace LifeSim.Core.Events;

/// <summary>
/// The live set of active environmental event modifiers (lifesim.md §6, §7). Owns the Environment
/// phase's aging/expiry and per-tick occurrence rolls, and exposes the aggregate effects the rest
/// of the tick reads: whether ground regen is halted (blight), whether density drains apply
/// (plague), the effective temperature offset (climatic anomaly), and the Global Stress Level
/// sensor value fed to every brain (lifesim.md §13).
/// </summary>
public sealed class EnvironmentState
{
    private readonly List<EnvironmentModifier> _modifiers;

    public EnvironmentState() => _modifiers = [];

    public EnvironmentState(IEnumerable<EnvironmentModifier> modifiers)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        _modifiers = [.. modifiers];
    }

    /// <summary>Active modifiers in insertion order, which is deterministic given the tick history.</summary>
    public IReadOnlyList<EnvironmentModifier> Modifiers => _modifiers;

    /// <summary>True while any Resource Blight is active — ground energy regeneration is suspended (lifesim.md §6).</summary>
    public bool BlightActive => HasActive(EventType.ResourceBlight);

    /// <summary>True while any Density-Dependent Plague is active — crowded organisms take an extra drain (lifesim.md §6).</summary>
    public bool PlagueActive => HasActive(EventType.DensityPlague);

    /// <summary>Sum of every active Climatic Anomaly's signed temperature shift, added to tile temperature (lifesim.md §6).</summary>
    public double TemperatureOffset =>
        _modifiers.Where(m => m.Type == EventType.ClimaticAnomaly).Sum(m => m.Magnitude);

    /// <summary>
    /// Global Stress Level sensor (lifesim.md §13): graded by how many event types are active,
    /// saturating at 1.0 when all three are (at most one modifier per type is ever active).
    /// </summary>
    public double GlobalStress => Math.Min(1.0, _modifiers.Count / 3.0);

    /// <summary>
    /// The Environment phase (lifesim.md §7): first age active modifiers and drop expired ones,
    /// then roll for newly-triggered events against the events PRNG stream in a fixed order —
    /// blight, plague, anomaly (lifesim.md §6, §9). At most one modifier of each type is active at
    /// a time; the occurrence roll is still consumed when one is already active, so draw counts
    /// stay stable regardless of world history.
    /// </summary>
    public void RunEnvironmentPhase(Prng eventsStream, EventsConfig config, long currentTick)
    {
        ArgumentNullException.ThrowIfNull(eventsStream);
        ArgumentNullException.ThrowIfNull(config);

        AgeAndExpire();

        TryTrigger(eventsStream, config.BlightProbability, EventType.ResourceBlight, config.BlightDurationTicks, currentTick);
        TryTrigger(eventsStream, config.PlagueProbability, EventType.DensityPlague, config.PlagueDurationTicks, currentTick);
        TryTriggerAnomaly(eventsStream, config, currentTick);
    }

    private void AgeAndExpire()
    {
        for (int i = _modifiers.Count - 1; i >= 0; i--)
        {
            int remaining = _modifiers[i].RemainingTicks - 1;
            if (remaining <= 0)
            {
                _modifiers.RemoveAt(i);
            }
            else
            {
                _modifiers[i] = _modifiers[i] with { RemainingTicks = remaining };
            }
        }
    }

    private void TryTrigger(Prng eventsStream, double probability, EventType type, int durationTicks, long currentTick)
    {
        bool triggered = eventsStream.NextDouble() < probability;
        if (triggered && !HasActive(type))
        {
            _modifiers.Add(new EnvironmentModifier
            {
                Type = type,
                StartTick = currentTick,
                RemainingTicks = durationTicks,
                Magnitude = 0.0,
            });
        }
    }

    private void TryTriggerAnomaly(Prng eventsStream, EventsConfig config, long currentTick)
    {
        bool triggered = eventsStream.NextDouble() < config.TemperatureAnomalyProbability;
        if (!triggered || HasActive(EventType.ClimaticAnomaly))
        {
            return;
        }

        // Heatwave (+) or ice age (−), chosen from the same events stream.
        double sign = eventsStream.NextDouble() < 0.5 ? -1.0 : 1.0;
        _modifiers.Add(new EnvironmentModifier
        {
            Type = EventType.ClimaticAnomaly,
            StartTick = currentTick,
            RemainingTicks = config.TemperatureAnomalyDurationTicks,
            Magnitude = sign * config.TemperatureAnomalyMagnitude,
        });
    }

    private bool HasActive(EventType type) => _modifiers.Exists(m => m.Type == type);
}
