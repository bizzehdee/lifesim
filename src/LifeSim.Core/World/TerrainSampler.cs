using System.Collections.Concurrent;
using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;

namespace LifeSim.Core.World;

/// <summary>
/// Reconstructs the implicit terrain (moisture, temperature, biome) for any tile from the world
/// seed and noise configuration alone — nothing here is persisted. The two
/// noise layers are seeded from the world seed via a fixed, non-PRNG-consuming mix so terrain is
/// stable regardless of how far gameplay PRNG streams have advanced.
/// </summary>
public sealed class TerrainSampler
{
    private readonly SimplexNoise _moistureNoise;
    private readonly SimplexNoise _temperatureNoise;
    private readonly SimulationConfig _config;

    // Terrain is static, so biome and physical temperature are pure functions of (x, y). They are read
    // per-organism every tick (metabolism + sensing) and per-tile every render — and the temperature
    // gradient blurs 25 tiles per query — so memoize them. Thread-safe for the engine + UI threads.
    private readonly ConcurrentDictionary<(int X, int Y), Biome> _biomeCache = new();
    private readonly ConcurrentDictionary<(int X, int Y), double> _temperatureCelsiusCache = new();

    public TerrainSampler(ulong worldSeed, SimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _moistureNoise = new SimplexNoise(DeriveLayerSeed(worldSeed, 1));
        _temperatureNoise = new SimplexNoise(DeriveLayerSeed(worldSeed, 2));
    }

    public double MoistureAt(int x, int y) => _moistureNoise.SampleFractal(x, y, _config.MoistureNoise);

    /// <summary>
    /// The raw temperature-noise field (~[-1, 1]) that drives biome *classification* against the
    /// cold/hot thresholds. This is the climate axis of the biome matrix, not a
    /// physical temperature — for that, see <see cref="TemperatureCelsiusAt"/>.
    /// </summary>
    public double TemperatureAt(int x, int y) => _temperatureNoise.SampleFractal(x, y, _config.TemperatureNoise);

    public Biome BiomeAt(int x, int y) =>
        _biomeCache.GetOrAdd((x, y), k => BiomeClassifier.Classify(MoistureAt(k.X, k.Y), TemperatureAt(k.X, k.Y), _config.Biomes.Thresholds));

    /// <summary>
    /// The tile's physical temperature in °C: its biome's baseline temperature
    /// plus the temperature-noise field scaled by <see cref="BiomesConfig.TemperatureVariation"/>.
    /// This is what thermal-stress metabolism and the temperature sensor read, so an organism's °C
    /// <c>thermal_center</c> is compared against a °C tile temperature — a Desert tile (hot) genuinely
    /// stresses cold-adapted organisms, an Ice Sheet (cold) stresses warm-adapted ones.
    /// </summary>
    public double TemperatureCelsiusAt(int x, int y) =>
        _temperatureCelsiusCache.GetOrAdd((x, y), k => SmoothedBiomeTemperature(k.X, k.Y) + (TemperatureAt(k.X, k.Y) * _config.Biomes.TemperatureVariation));

    /// <summary>
    /// The biome baseline temperature at (x,y), box-blurred over
    /// <see cref="BiomesConfig.TemperatureGradientRadius"/> so biome borders read as gradients, not
    /// walls. Radius 0 is the raw per-biome temperature; a tile whose whole kernel is one biome is
    /// unchanged, so only the margins soften.
    /// </summary>
    private double SmoothedBiomeTemperature(int x, int y)
    {
        int radius = _config.Biomes.TemperatureGradientRadius;
        if (radius <= 0)
        {
            return _config.Biomes.For(BiomeAt(x, y)).Temperature;
        }

        double sum = 0.0;
        int count = 0;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                sum += _config.Biomes.For(BiomeAt(x + dx, y + dy)).Temperature;
                count++;
            }
        }

        return sum / count;
    }

    /// <summary>
    /// Samples a rectangular window of tiles for inspection — never required for normal replay, since terrain is always reconstructable.
    /// </summary>
    public List<DebugTileEntry> CaptureDebugGrid(int x0, int y0, int width, int height)
    {
        var tiles = new List<DebugTileEntry>(width * height);
        for (int y = y0; y < y0 + height; y++)
        {
            for (int x = x0; x < x0 + width; x++)
            {
                double moisture = MoistureAt(x, y);
                double temperature = TemperatureAt(x, y);
                Biome biome = BiomeClassifier.Classify(moisture, temperature, _config.Biomes.Thresholds);
                tiles.Add(new DebugTileEntry(x, y, biome, moisture, temperature));
            }
        }

        return tiles;
    }

    // Mixes the world seed with a fixed per-layer salt (SplitMix64 finalizer) so the moisture and
    // temperature fields are decorrelated without consuming any PRNG stream state.
    private static ulong DeriveLayerSeed(ulong worldSeed, ulong salt) =>
        SplitMix64.Finalize(worldSeed + (salt * 0x9E3779B97F4A7C15UL));
}

/// <summary>A single sampled tile, used only by the optional debug snapshot cache.</summary>
public sealed record DebugTileEntry(int X, int Y, Biome Biome, double Moisture, double Temperature);
