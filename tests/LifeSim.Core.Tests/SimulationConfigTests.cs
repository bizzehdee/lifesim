using LifeSim.Core.Configuration;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class SimulationConfigTests
{
    [Fact]
    public void Default_hasAppendixADefaults()
    {
        SimulationConfig config = SimulationConfig.Default;

        Assert.Equal(200, config.InitialPopulation);
        Assert.True(config.Senescence); // aging on by default
        Assert.Equal(3, config.Reproduction.ReproductionCooldownTicks);
        Assert.Equal(20.0, config.Events.TemperatureAnomalyMagnitude);
    }

    [Fact]
    public void Biomes_reflectTheirCharacteristics()
    {
        BiomesConfig biomes = SimulationConfig.Default.Biomes;

        // Ice sheet has zero regeneration.
        Assert.Equal(0.0, biomes.For(Biome.IceSheet).RegenRate);
        // Swamp is energy-dense but high friction.
        Assert.True(biomes.For(Biome.Swamp).RegenRate > biomes.For(Biome.Grassland).RegenRate);
        Assert.True(biomes.For(Biome.Swamp).Friction > biomes.For(Biome.Grassland).Friction);
        // Desert is hot.
        Assert.True(biomes.For(Biome.Desert).Temperature > biomes.For(Biome.Grassland).Temperature);
    }
}
