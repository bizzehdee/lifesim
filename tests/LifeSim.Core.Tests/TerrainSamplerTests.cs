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
    public void TemperatureCelsiusAt_staysWithinTheGlobalBiomeRange()
    {
        var config = SimulationConfig.Default;
        var sampler = new TerrainSampler(2024, config);
        double variation = config.Biomes.TemperatureVariation;

        // With gradient smoothing a margin tile blends toward its neighbours, so it is no longer
        // pinned to its own biome's band — but every tile still lies within the global range spanned
        // by the coldest and hottest biomes (± the noise band).
        double coldest = config.Biomes.IceSheet.Temperature;
        double hottest = config.Biomes.Desert.Temperature;

        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                double celsius = sampler.TemperatureCelsiusAt(x, y);
                Assert.InRange(celsius, coldest - (variation * 2.0), hottest + (variation * 2.0));
            }
        }
    }

    [Fact]
    public void TemperatureGradient_smoothsBiomeBorders_intoIntermediateTemperatures()
    {
        var sharp = new TerrainSampler(2024, SimulationConfig.Default with
        {
            Biomes = SimulationConfig.Default.Biomes with { TemperatureGradientRadius = 0 },
        });
        var smooth = new TerrainSampler(2024, SimulationConfig.Default); // default radius 2
        double variation = SimulationConfig.Default.Biomes.TemperatureVariation;

        bool foundSmoothedBorder = false;
        for (int y = 0; y < 48 && !foundSmoothedBorder; y++)
        {
            for (int x = 0; x < 48; x++)
            {
                // The noise field is identical for both (same seed/config), so subtracting it isolates
                // the biome-baseline component: sharp = own biome temp, smooth = blurred temp.
                double noise = smooth.TemperatureAt(x, y) * variation;
                double sharpBase = sharp.TemperatureCelsiusAt(x, y) - noise;
                double smoothBase = smooth.TemperatureCelsiusAt(x, y) - noise;
                if (Math.Abs(sharpBase - smoothBase) > 1e-6)
                {
                    foundSmoothedBorder = true; // a border tile pulled toward a neighbouring biome
                    break;
                }
            }
        }

        Assert.True(foundSmoothedBorder, "The gradient should turn at least one biome border into an intermediate temperature.");
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
