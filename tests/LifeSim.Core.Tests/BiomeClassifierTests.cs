using LifeSim.Core.Configuration;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class BiomeClassifierTests
{
    private static readonly BiomeThresholds Thresholds = new();

    [Theory]
    // Low moisture.
    [InlineData(-0.5, -0.9, Biome.IceSheet)]
    [InlineData(-0.5, 0.0, Biome.Grassland)]
    [InlineData(-0.5, 0.9, Biome.Desert)]
    // High moisture.
    [InlineData(0.5, -0.9, Biome.IceSheet)]
    [InlineData(0.5, 0.0, Biome.Swamp)]
    [InlineData(0.5, 0.9, Biome.Swamp)]
    public void Classify_matchesBiomeMatrix(double moisture, double temperature, Biome expected)
    {
        Assert.Equal(expected, BiomeClassifier.Classify(moisture, temperature, Thresholds));
    }
}
