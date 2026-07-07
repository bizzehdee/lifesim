using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Events;

namespace LifeSim.Core.Tests;

public class EnvironmentStateTests
{
    // Force a given event to always/never fire by pinning its probability to 1.0 / 0.0.
    private static EventsConfig Never() => new EventsConfig() with
    {
        BlightProbability = 0.0,
        PlagueProbability = 0.0,
        TemperatureAnomalyProbability = 0.0,
    };

    [Fact]
    public void RunEnvironmentPhase_withZeroProbabilities_triggersNothing()
    {
        var state = new EnvironmentState();

        for (int tick = 1; tick <= 100; tick++)
        {
            state.RunEnvironmentPhase(new Prng((ulong)tick), Never(), tick);
        }

        Assert.Empty(state.Modifiers);
        Assert.False(state.BlightActive);
        Assert.False(state.PlagueActive);
        Assert.Equal(0.0, state.TemperatureOffset);
        Assert.Equal(0.0, state.GlobalStress);
    }

    [Fact]
    public void RunEnvironmentPhase_certainBlight_activatesThenExpiresAfterItsDuration()
    {
        EventsConfig config = Never() with { BlightProbability = 1.0, BlightDurationTicks = 5 };
        var state = new EnvironmentState();
        var events = new Prng(1);

        state.RunEnvironmentPhase(events, config, 1);
        Assert.True(state.BlightActive);

        // Already active on subsequent ticks — the roll is consumed but no second modifier is added.
        for (int tick = 2; tick <= 5; tick++)
        {
            state.RunEnvironmentPhase(events, config, tick);
            Assert.Single(state.Modifiers);
            Assert.True(state.BlightActive);
        }

        // Tick 6's Environment phase ages the 5-tick modifier out (started tick 1, 5 ticks active).
        EventsConfig noRetrigger = config with { BlightProbability = 0.0 };
        state.RunEnvironmentPhase(events, noRetrigger, 6);
        Assert.False(state.BlightActive);
        Assert.Empty(state.Modifiers);
    }

    [Fact]
    public void RunEnvironmentPhase_climaticAnomaly_carriesASignedMagnitudeAndShiftsTemperature()
    {
        EventsConfig config = Never() with
        {
            TemperatureAnomalyProbability = 1.0,
            TemperatureAnomalyDurationTicks = 10,
            TemperatureAnomalyMagnitude = 20.0,
        };
        var state = new EnvironmentState();

        state.RunEnvironmentPhase(new Prng(1), config, 1);

        EnvironmentModifier anomaly = Assert.Single(state.Modifiers);
        Assert.Equal(EventType.ClimaticAnomaly, anomaly.Type);
        Assert.Equal(20.0, Math.Abs(anomaly.Magnitude));
        Assert.Equal(anomaly.Magnitude, state.TemperatureOffset);
    }

    [Fact]
    public void GlobalStress_gradesUpWithTheNumberOfActiveEventTypes()
    {
        EventsConfig allCertain = new EventsConfig() with
        {
            BlightProbability = 1.0,
            PlagueProbability = 1.0,
            TemperatureAnomalyProbability = 1.0,
            BlightDurationTicks = 50,
            PlagueDurationTicks = 50,
            TemperatureAnomalyDurationTicks = 50,
        };
        var state = new EnvironmentState();

        state.RunEnvironmentPhase(new Prng(1), allCertain, 1);

        Assert.True(state.BlightActive);
        Assert.True(state.PlagueActive);
        Assert.Equal(3, state.Modifiers.Count);
        Assert.Equal(1.0, state.GlobalStress);
    }

    [Fact]
    public void RunEnvironmentPhase_isDeterministicForTheSameStreamState()
    {
        EventsConfig config = new EventsConfig() with
        {
            BlightProbability = 0.5,
            PlagueProbability = 0.5,
            TemperatureAnomalyProbability = 0.5,
        };

        var a = new EnvironmentState();
        var b = new EnvironmentState();
        var streamA = new Prng(2024);
        var streamB = new Prng(2024);

        for (int tick = 1; tick <= 30; tick++)
        {
            a.RunEnvironmentPhase(streamA, config, tick);
            b.RunEnvironmentPhase(streamB, config, tick);
        }

        Assert.Equal(a.Modifiers, b.Modifiers);
    }

    [Fact]
    public void Constructor_rehydratesActiveModifiersFromAPriorSnapshot()
    {
        var restored = new EnvironmentState(
        [
            new EnvironmentModifier { Type = EventType.ResourceBlight, StartTick = 3, RemainingTicks = 10, Magnitude = 0.0 },
            new EnvironmentModifier { Type = EventType.ClimaticAnomaly, StartTick = 3, RemainingTicks = 8, Magnitude = -20.0 },
        ]);

        Assert.True(restored.BlightActive);
        Assert.Equal(-20.0, restored.TemperatureOffset);
    }
}
