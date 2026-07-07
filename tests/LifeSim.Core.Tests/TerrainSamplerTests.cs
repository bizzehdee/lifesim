using LifeSim.Core.Configuration;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class TerrainSamplerTests
{
    [Fact]
    public void SameSeed_producesIdenticalBiomeMap()
    {
        var config = SimulationConfig.Default;
        var a = new TerrainSampler(2024, config);
        var b = new TerrainSampler(2024, config);

        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                Assert.Equal(a.BiomeAt(x, y), b.BiomeAt(x, y));
                Assert.Equal(a.MoistureAt(x, y), b.MoistureAt(x, y));
                Assert.Equal(a.TemperatureAt(x, y), b.TemperatureAt(x, y));
            }
        }
    }

    [Fact]
    public void DifferentSeeds_produceDifferentBiomeMaps()
    {
        var config = SimulationConfig.Default;
        var a = new TerrainSampler(1, config);
        var b = new TerrainSampler(2, config);

        // Default noise frequency (0.01) means a small tile window barely moves across the
        // field, so sample widely spaced coordinates rather than a dense small grid.
        bool anyDifferent = false;
        for (int i = 0; i < 200 && !anyDifferent; i++)
        {
            int x = i * 37;
            int y = i * 53;
            anyDifferent = a.BiomeAt(x, y) != b.BiomeAt(x, y);
        }

        Assert.True(anyDifferent);
    }

    [Fact]
    public void MoistureAndTemperature_areDecorrelated()
    {
        // The two noise layers must not just be the same field twice, or the matrix collapses.
        var sampler = new TerrainSampler(99, SimulationConfig.Default);

        bool anyDifferent = false;
        for (int y = 0; y < 16 && !anyDifferent; y++)
        {
            for (int x = 0; x < 16 && !anyDifferent; x++)
            {
                anyDifferent = sampler.MoistureAt(x, y) != sampler.TemperatureAt(x, y);
            }
        }

        Assert.True(anyDifferent);
    }

    [Fact]
    public void TemperatureCelsiusAt_tracksTheBiomeBaselineWithinItsVariationBand()
    {
        var config = SimulationConfig.Default;
        var sampler = new TerrainSampler(2024, config);
        double variation = config.Biomes.TemperatureVariation;

        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                double baseline = config.Biomes.For(sampler.BiomeAt(x, y)).Temperature;
                double celsius = sampler.TemperatureCelsiusAt(x, y);

                // Temperature-noise field is ~[-1, 1], so a tile stays within ±(variation·~1) of its
                // biome baseline — i.e. biomes are genuinely thermally distinct, not one flat field.
                Assert.True(Math.Abs(celsius - baseline) <= (variation * 2.0) + 1e-9);
            }
        }
    }

    [Fact]
    public void TemperatureCelsiusAt_isColderOnIceThanDesert_soThermalPressureIsBiomeSpecific()
    {
        var config = SimulationConfig.Default;
        var sampler = new TerrainSampler(2024, config);

        // Desert is the hottest biome baseline (45°C), Ice Sheet the coldest (−15°C); with a small
        // within-biome variation band they can never cross, so a desert tile is always hotter.
        double variation = config.Biomes.TemperatureVariation;
        Assert.True(config.Biomes.Desert.Temperature - variation > config.Biomes.IceSheet.Temperature + variation);
    }

    [Fact]
    public void CaptureDebugGrid_matchesPerTileSamples()
    {
        var sampler = new TerrainSampler(7, SimulationConfig.Default);

        List<DebugTileEntry> tiles = sampler.CaptureDebugGrid(x0: 2, y0: 3, width: 4, height: 4);

        Assert.Equal(16, tiles.Count);
        foreach (DebugTileEntry tile in tiles)
        {
            Assert.Equal(sampler.BiomeAt(tile.X, tile.Y), tile.Biome);
            Assert.Equal(sampler.MoistureAt(tile.X, tile.Y), tile.Moisture);
            Assert.Equal(sampler.TemperatureAt(tile.X, tile.Y), tile.Temperature);
        }
    }
}
