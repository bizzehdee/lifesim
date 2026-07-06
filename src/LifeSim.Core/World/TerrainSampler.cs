using LifeSim.Core.Configuration;

namespace LifeSim.Core.World;

/// <summary>
/// Reconstructs the implicit terrain (moisture, temperature, biome) for any tile from the world
/// seed and noise configuration alone — nothing here is persisted (lifesim.md §2, §12). The two
/// noise layers are seeded from the world seed via a fixed, non-PRNG-consuming mix so terrain is
/// stable regardless of how far gameplay PRNG streams have advanced.
/// </summary>
public sealed class TerrainSampler
{
    private readonly SimplexNoise _moistureNoise;
    private readonly SimplexNoise _temperatureNoise;
    private readonly SimulationConfig _config;

    public TerrainSampler(ulong worldSeed, SimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _moistureNoise = new SimplexNoise(DeriveLayerSeed(worldSeed, 1));
        _temperatureNoise = new SimplexNoise(DeriveLayerSeed(worldSeed, 2));
    }

    public double MoistureAt(int x, int y) => _moistureNoise.SampleFractal(x, y, _config.MoistureNoise);

    public double TemperatureAt(int x, int y) => _temperatureNoise.SampleFractal(x, y, _config.TemperatureNoise);

    public Biome BiomeAt(int x, int y) =>
        BiomeClassifier.Classify(MoistureAt(x, y), TemperatureAt(x, y), _config.Biomes.Thresholds);

    /// <summary>
    /// Samples a rectangular window of tiles for inspection (lifesim.md §12's debug snapshot
    /// mode) — never required for normal replay, since terrain is always reconstructable.
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
    private static ulong DeriveLayerSeed(ulong worldSeed, ulong salt)
    {
        ulong x = worldSeed + (salt * 0x9E3779B97F4A7C15UL);
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }
}

/// <summary>A single sampled tile, used only by the optional debug snapshot cache (lifesim.md §12).</summary>
public sealed record DebugTileEntry(int X, int Y, Biome Biome, double Moisture, double Temperature);
