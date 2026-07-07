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

        // Ice sheet is the harshest biome — a minimal, non-zero energy trickle, poorer than the desert.
        Assert.True(biomes.For(Biome.IceSheet).RegenRate > 0.0);
        Assert.True(biomes.For(Biome.IceSheet).RegenRate < biomes.For(Biome.Desert).RegenRate);
        Assert.True(biomes.For(Biome.IceSheet).EnergyCap > 0.0);
        Assert.True(biomes.For(Biome.IceSheet).EnergyCap < biomes.For(Biome.Desert).EnergyCap);
        // Swamp is energy-dense but high friction.
        Assert.True(biomes.For(Biome.Swamp).RegenRate > biomes.For(Biome.Grassland).RegenRate);
        Assert.True(biomes.For(Biome.Swamp).Friction > biomes.For(Biome.Grassland).Friction);
        // Desert is hot.
        Assert.True(biomes.For(Biome.Desert).Temperature > biomes.For(Biome.Grassland).Temperature);
    }
}
