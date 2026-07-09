using LifeSim.Core.Configuration;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class EnvironmentClockTests
{
    // Isolates the day cycle: no seasonal dimming or seasonal temperature swing, and a year long enough
    // that the sampled ticks all sit at season phase ~0 (deep winter baseline).
    private static readonly EnvironmentCycleConfig DayOnly = new()
    {
        DayLengthTicks = 100,
        YearLengthTicks = 1_000_000,
        DayNightTemperatureAmplitude = 10.0,
        SeasonalTemperatureAmplitude = 0.0,
        NightLightFloor = 0.0,
        WinterLightScale = 1.0, // no seasonal dimming, so daytime peak is full light
    };

    [Fact]
    public void At_midnight_isDarkestAndColdest()
    {
        EnvironmentClock clock = EnvironmentClock.At(0, DayOnly);

        Assert.Equal(0.0, clock.DayPhase, precision: 9);
        Assert.Equal(0.0, clock.GlobalLight, precision: 9);      // night floor
        Assert.Equal(-5.0, clock.CyclicTemperatureOffset, precision: 9); // amplitude 10 * (0 - 0.5)
    }

    [Fact]
    public void At_noon_isBrightestAndWarmest()
    {
        EnvironmentClock clock = EnvironmentClock.At(50, DayOnly); // half of DayLengthTicks

        Assert.Equal(0.5, clock.DayPhase, precision: 9);
        Assert.Equal(1.0, clock.GlobalLight, precision: 9);      // full daytime light
        Assert.Equal(5.0, clock.CyclicTemperatureOffset, precision: 9);  // amplitude 10 * (1 - 0.5)
    }

    [Fact]
    public void Season_dimsDaytimeLightInWinterVsSummer()
    {
        var config = new EnvironmentCycleConfig
        {
            DayLengthTicks = 100,
            YearLengthTicks = 1000,
            NightLightFloor = 0.0,
            WinterLightScale = 0.25,
            SeasonalTemperatureAmplitude = 20.0,
            DayNightTemperatureAmplitude = 0.0,
        };

        // Both cycles are phase-locked at tick 0, so exact midwinter-noon isn't reachable; instead compare
        // the same time of day (noon) early in the year vs mid-year, isolating the seasonal effect.
        EnvironmentClock winterNoon = EnvironmentClock.At(50, config);  // day 0.5, season ~0.05 (winter)
        EnvironmentClock summerNoon = EnvironmentClock.At(550, config); // day 0.5, season ~0.55 (summer)

        Assert.Equal(0.5, winterNoon.DayPhase, precision: 9);
        Assert.Equal(0.5, summerNoon.DayPhase, precision: 9);

        // Winter noon is dimmed toward WinterLightScale; summer noon is near full light.
        Assert.InRange(winterNoon.GlobalLight, 0.25, 0.35);
        Assert.True(summerNoon.GlobalLight > 0.95);

        // And midsummer is warmer than midwinter (temperature swings on the seasonal axis).
        Assert.True(summerNoon.CyclicTemperatureOffset > winterNoon.CyclicTemperatureOffset);
    }

    [Fact]
    public void Is_periodic_andPure()
    {
        EnvironmentCycleConfig config = SimulationConfig.Default.Cycle;
        long period = (long)config.YearLengthTicks; // a multiple of DayLengthTicks by default (4800 / 240)

        Assert.Equal(0L, period % config.DayLengthTicks); // guards the periodicity assumption

        // Pure: same tick → identical result. Periodic: one full year later → identical result.
        Assert.Equal(EnvironmentClock.At(1234, config), EnvironmentClock.At(1234, config));
        Assert.Equal(EnvironmentClock.At(0, config), EnvironmentClock.At(period, config));
    }

    [Fact]
    public void GlobalLight_staysWithinUnitRange_acrossAWholeYear()
    {
        EnvironmentCycleConfig config = SimulationConfig.Default.Cycle;

        for (long tick = 0; tick <= config.YearLengthTicks; tick += 37)
        {
            double light = EnvironmentClock.At(tick, config).GlobalLight;
            Assert.InRange(light, 0.0, 1.0);
        }
    }

    [Fact]
    public void FlatConfig_producesConstantLightAndNoTemperatureSwing()
    {
        // The documented "flat world" escape hatch: zero amplitudes, light floors at 1.
        var flat = new EnvironmentCycleConfig
        {
            DayNightTemperatureAmplitude = 0.0,
            SeasonalTemperatureAmplitude = 0.0,
            NightLightFloor = 1.0,
            WinterLightScale = 1.0,
        };

        foreach (long tick in new long[] { 0, 13, 120, 2400, 4801 })
        {
            EnvironmentClock clock = EnvironmentClock.At(tick, flat);
            Assert.Equal(1.0, clock.GlobalLight, precision: 9);
            Assert.Equal(0.0, clock.CyclicTemperatureOffset, precision: 9);
        }
    }
}
