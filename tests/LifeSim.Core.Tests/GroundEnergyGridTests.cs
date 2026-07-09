using LifeSim.Core.Configuration;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class GroundEnergyGridTests
{
    private static (TerrainSampler Terrain, SimulationConfig Config) NewWorld() =>
        (new TerrainSampler(42, SimulationConfig.Default), SimulationConfig.Default);

    [Fact]
    public void UntouchedTile_startsAtBiomeCap()
    {
        (TerrainSampler terrain, SimulationConfig config) = NewWorld();
        var grid = new GroundEnergyGrid(terrain, config);

        double cap = grid.CapAt(5, 5);
        Assert.Equal(cap, grid.EnergyAt(5, 5));
        Assert.Empty(grid.CaptureState());
    }

    [Fact]
    public void Drain_reducesEnergy_andNeverGoesNegative()
    {
        (TerrainSampler terrain, SimulationConfig config) = NewWorld();
        var grid = new GroundEnergyGrid(terrain, config);
        double cap = grid.CapAt(1, 1);

        double taken = grid.Drain(1, 1, cap + 100.0);

        Assert.Equal(cap, taken);
        Assert.Equal(0.0, grid.EnergyAt(1, 1));
    }

    [Fact]
    public void RegenerateTick_movesTowardCap_andStopsAtCap()
    {
        (TerrainSampler terrain, SimulationConfig config) = NewWorld();
        // Grassland regenerates; find a grassland tile.
        int x = FindTileOfBiome(terrain, Biome.Grassland);
        int y = 0;

        var grid = new GroundEnergyGrid(terrain, config);
        double cap = grid.CapAt(x, y);
        grid.Drain(x, y, cap);
        Assert.Equal(0.0, grid.EnergyAt(x, y));

        for (int i = 0; i < 10_000; i++)
        {
            grid.RegenerateTick(globalLight: 1.0, photosynthesis: false);
        }

        Assert.Equal(cap, grid.EnergyAt(x, y));
        // Regen never overshoots the cap once fully recovered, and the tile drops out of the
        // sparse override map again once it's back to its implicit full state.
        Assert.Empty(grid.CaptureState());
    }

    [Fact]
    public void RegenerateTick_photosynthesis_scalesRegenByLocalLight()
    {
        (TerrainSampler terrain, SimulationConfig config) = NewWorld();
        int x = FindTileOfBiome(terrain, Biome.Grassland); // LightFactor 1.0
        int y = 0;
        double rate = config.Biomes.For(Biome.Grassland).RegenRate;

        // Photosynthesis off: plain biome rate regardless of light (unchanged behaviour).
        var off = new GroundEnergyGrid(terrain, config);
        off.Drain(x, y, off.CapAt(x, y));
        off.RegenerateTick(globalLight: 0.5, photosynthesis: false);
        Assert.Equal(rate, off.EnergyAt(x, y), precision: 9);

        // On at half light: half the regen (grassland LightFactor is 1.0).
        var half = new GroundEnergyGrid(terrain, config);
        half.Drain(x, y, half.CapAt(x, y));
        half.RegenerateTick(globalLight: 0.5, photosynthesis: true);
        Assert.Equal(rate * 0.5, half.EnergyAt(x, y), precision: 9);

        // On in the dark: regen stalls entirely.
        var dark = new GroundEnergyGrid(terrain, config);
        dark.Drain(x, y, dark.CapAt(x, y));
        dark.RegenerateTick(globalLight: 0.0, photosynthesis: true);
        Assert.Equal(0.0, dark.EnergyAt(x, y), precision: 9);
    }

    [Fact]
    public void IceSheet_hasOnlyAMinimalEnergyTrickle()
    {
        (TerrainSampler terrain, SimulationConfig config) = NewWorld();
        int x = FindTileOfBiome(terrain, Biome.IceSheet);
        int y = 0;

        // Barely productive, but not barren: a small non-zero cap that regenerates far slower than
        // grassland, so only a lean, cold-adapted lineage can live off it.
        double iceCap = config.Biomes.For(Biome.IceSheet).EnergyCap;
        double grassCap = config.Biomes.For(Biome.Grassland).EnergyCap;
        Assert.True(iceCap > 0.0 && iceCap < grassCap);

        var grid = new GroundEnergyGrid(terrain, config);
        Assert.Equal(iceCap, grid.EnergyAt(x, y)); // starts full at its (tiny) cap

        grid.Drain(x, y, iceCap);
        Assert.Equal(0.0, grid.EnergyAt(x, y));

        grid.RegenerateTick(globalLight: 1.0, photosynthesis: false);
        double afterOneTick = grid.EnergyAt(x, y);
        Assert.True(afterOneTick > 0.0 && afterOneTick < iceCap); // regenerates, but only a trickle
    }

    [Fact]
    public void CaptureState_andFromState_roundTrip()
    {
        (TerrainSampler terrain, SimulationConfig config) = NewWorld();
        var grid = new GroundEnergyGrid(terrain, config);
        grid.Drain(1, 1, 3.0);
        grid.Drain(2, 2, 1.5);

        List<GroundEnergyEntry> state = grid.CaptureState();
        var restored = GroundEnergyGrid.FromState(terrain, config, state);

        Assert.Equal(grid.EnergyAt(1, 1), restored.EnergyAt(1, 1));
        Assert.Equal(grid.EnergyAt(2, 2), restored.EnergyAt(2, 2));
        Assert.Equal(grid.EnergyAt(9, 9), restored.EnergyAt(9, 9)); // both at implicit cap
    }

    private static int FindTileOfBiome(TerrainSampler terrain, Biome biome)
    {
        for (int x = 0; x < 10_000; x++)
        {
            if (terrain.BiomeAt(x, 0) == biome)
            {
                return x;
            }
        }

        throw new InvalidOperationException($"No {biome} tile found along y=0 within range; adjust the search or thresholds.");
    }
}
