using LifeSim.Core.Configuration;

namespace LifeSim.Core.World;

/// <summary>
/// The per-tile ambient energy buffer (lifesim.md §2, §11). Every tile starts full at its
/// biome's cap; only tiles that have been drained or topped up below cap need to be tracked, so
/// state is a sparse override map rather than a dense grid — the vast majority of tiles are never
/// visited and stay implicitly at their cap.
/// </summary>
public sealed class GroundEnergyGrid
{
    private readonly Dictionary<(int X, int Y), double> _overrides = [];
    private readonly TerrainSampler _terrain;
    private readonly SimulationConfig _config;

    public GroundEnergyGrid(TerrainSampler terrain, SimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(config);
        _terrain = terrain;
        _config = config;
    }

    public double CapAt(int x, int y) => _config.Biomes.For(_terrain.BiomeAt(x, y)).EnergyCap;

    public double EnergyAt(int x, int y) =>
        _overrides.TryGetValue((x, y), out double value) ? value : CapAt(x, y);

    /// <summary>Removes up to <paramref name="amount"/> energy from a tile; returns what was actually taken.</summary>
    public double Drain(int x, int y, double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        double available = EnergyAt(x, y);
        double taken = Math.Min(available, amount);
        SetEnergy(x, y, available - taken);
        return taken;
    }

    /// <summary>Adds energy to a tile, clamped to its biome cap (e.g. corpse energy deposits — lifesim.md §11).</summary>
    public void Deposit(int x, int y, double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        SetEnergy(x, y, Math.Min(CapAt(x, y), EnergyAt(x, y) + amount));
    }

    /// <summary>Regenerates every tracked tile toward its biome cap by one tick's regen rate (lifesim.md §2, §7).</summary>
    public void RegenerateTick()
    {
        if (_overrides.Count == 0)
        {
            return;
        }

        foreach ((int X, int Y) key in _overrides.Keys.ToArray())
        {
            double rate = _config.Biomes.For(_terrain.BiomeAt(key.X, key.Y)).RegenRate;
            SetEnergy(key.X, key.Y, _overrides[key] + rate);
        }
    }

    /// <summary>Captures every tile that currently deviates from its implicit cap, sorted for deterministic output.</summary>
    public List<GroundEnergyEntry> CaptureState() =>
        _overrides
            .OrderBy(kv => kv.Key.Y)
            .ThenBy(kv => kv.Key.X)
            .Select(kv => new GroundEnergyEntry(kv.Key.X, kv.Key.Y, kv.Value))
            .ToList();

    /// <summary>Rehydrates a grid from a previously captured sparse override list.</summary>
    public static GroundEnergyGrid FromState(
        TerrainSampler terrain, SimulationConfig config, IEnumerable<GroundEnergyEntry> entries)
    {
        var grid = new GroundEnergyGrid(terrain, config);
        foreach (GroundEnergyEntry entry in entries)
        {
            grid.SetEnergy(entry.X, entry.Y, entry.Energy);
        }

        return grid;
    }

    private void SetEnergy(int x, int y, double value)
    {
        double cap = CapAt(x, y);
        double clamped = Math.Min(cap, Math.Max(0.0, value));
        if (clamped >= cap)
        {
            _overrides.Remove((x, y));
        }
        else
        {
            _overrides[(x, y)] = clamped;
        }
    }
}

/// <summary>One sparse ground-energy override, serialized as part of the snapshot (lifesim.md §12).</summary>
public sealed record GroundEnergyEntry(int X, int Y, double Energy);
