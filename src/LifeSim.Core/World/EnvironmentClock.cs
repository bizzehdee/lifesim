using LifeSim.Core.Configuration;

namespace LifeSim.Core.World;

/// <summary>
/// The deterministic day/night + seasonal clock: a pure function of the tick and
/// <see cref="EnvironmentCycleConfig"/>. It produces the global light level and the cyclic temperature
/// offset the world uses each tick. There is no randomness and no stored state, so the clock is
/// re-derived identically on load and needs no PRNG stream — the same "derived, not stored" property
/// that keeps replay and save/reload trivially correct.
/// <para>
/// Phase runs 0 → 1 over each cycle. Day: 0 = midnight (darkest/coldest), 0.5 = noon
/// (brightest/warmest). Season: 0 = midwinter, 0.5 = midsummer. The temperature offset is symmetric
/// about 0, so the cycle swings around the baseline biome climate rather than biasing its mean.
/// </para>
/// </summary>
public readonly record struct EnvironmentClock
{
    private const double TwoPi = 2.0 * Math.PI;

    private EnvironmentClock(double dayPhase, double seasonPhase, double globalLight, double cyclicTemperatureOffset)
    {
        DayPhase = dayPhase;
        SeasonPhase = seasonPhase;
        GlobalLight = globalLight;
        CyclicTemperatureOffset = cyclicTemperatureOffset;
    }

    /// <summary>Position within the current day, 0 (midnight) .. 1 (next midnight); noon is 0.5.</summary>
    public double DayPhase { get; }

    /// <summary>Position within the current year, 0 (midwinter) .. 1 (next midwinter); midsummer is 0.5.</summary>
    public double SeasonPhase { get; }

    /// <summary>
    /// Global light in [0, 1]: <see cref="EnvironmentCycleConfig.NightLightFloor"/> at midnight rising to
    /// the season's daytime peak at noon (that peak itself dims toward
    /// <see cref="EnvironmentCycleConfig.WinterLightScale"/> in winter).
    /// </summary>
    public double GlobalLight { get; }

    /// <summary>
    /// Temperature shift (°C) added to the baseline tile temperature: warmest at noon/midsummer, coldest
    /// at midnight/midwinter, averaging ~0 over a full cycle.
    /// </summary>
    public double CyclicTemperatureOffset { get; }

    /// <summary>Evaluate the clock at <paramref name="tick"/> for the given cycle configuration.</summary>
    public static EnvironmentClock At(long tick, EnvironmentCycleConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        long dayLength = Math.Max(1, config.DayLengthTicks);
        long yearLength = Math.Max(1, config.YearLengthTicks);

        // Non-negative modulo so a (hypothetical) negative tick still yields a phase in [0, 1).
        double dayPhase = (double)((((tick % dayLength) + dayLength) % dayLength)) / dayLength;
        double seasonPhase = (double)((((tick % yearLength) + yearLength) % yearLength)) / yearLength;

        // Raised-cosine factors: 0 at midnight/midwinter, 1 at noon/midsummer.
        double dayFactor = (1.0 - Math.Cos(TwoPi * dayPhase)) * 0.5;
        double seasonFactor = (1.0 - Math.Cos(TwoPi * seasonPhase)) * 0.5;

        // Daytime peak light dims in winter; within a day light rises from the night floor to that peak.
        double seasonalPeak = config.WinterLightScale + ((1.0 - config.WinterLightScale) * seasonFactor);
        double globalLight = Math.Clamp(
            config.NightLightFloor + ((seasonalPeak - config.NightLightFloor) * dayFactor), 0.0, 1.0);

        // Symmetric about 0 (factor - 0.5 spans -0.5..+0.5) so mean climate is unbiased by the cycle.
        double cyclicTemperatureOffset =
            (config.DayNightTemperatureAmplitude * (dayFactor - 0.5))
            + (config.SeasonalTemperatureAmplitude * (seasonFactor - 0.5));

        return new EnvironmentClock(dayPhase, seasonPhase, globalLight, cyclicTemperatureOffset);
    }
}
