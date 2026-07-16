using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Sensing;

/// <summary>
/// Builds the fixed, normalized sensory input vector from world state cached at
/// the start of the tick — callers must build every organism's vector before any organism's
/// intent is resolved, so decisions never see same-tick movement.
/// Gaussian noise scaled by <c>sensory_acuity</c> is injected last, from the dedicated
/// sensory-noise PRNG stream.
/// </summary>
public sealed class SensoryInputBuilder
{
    // Judgment calls (no single normalization scheme is specified): distance/size deltas normalize by the trait's own bound rather
    // than a fixed constant, so behavior doesn't quietly change as trait_bounds are retuned.
    private const double MaxNoiseStdDev = 0.3;
    private const double NearbyCountSaturation = 5.0;

    private readonly TerrainSampler _terrain;
    private readonly GroundEnergyGrid _groundEnergy;
    private readonly SimulationConfig _config;
    private readonly WorldState _world;

    public SensoryInputBuilder(TerrainSampler terrain, GroundEnergyGrid groundEnergy, SimulationConfig config, WorldState world)
    {
        _terrain = terrain;
        _groundEnergy = groundEnergy;
        _config = config;
        _world = world;
    }

    public double[] Build(
        Organism self, IReadOnlyDictionary<long, Organism> allOrganisms, long currentTick,
        Prng sensoryNoiseStream, double globalStress, double temperatureOffset, EnvironmentClock clock = default)
    {
        var values = new double[NeatTopology.InputCount];

        values[(int)SensoryField.Energy] = self.Energy / Organism.EnergyCeiling;
        values[(int)SensoryField.Age] = Math.Tanh(self.Age / 100.0);
        values[(int)SensoryField.TileTemperature] = (_terrain.TemperatureCelsiusAt(self.X, self.Y) + temperatureOffset) / 50.0;

        Biome biome = _terrain.BiomeAt(self.X, self.Y);
        values[(int)SensoryField.BiomeFriction] = _config.Biomes.For(biome).Friction / 5.0;

        (double distance, double dirX, double dirY) richestTile = FindRichestTile(self);
        values[(int)SensoryField.RichestTileDistance] = richestTile.distance;
        values[(int)SensoryField.RichestTileDirectionX] = richestTile.dirX;
        values[(int)SensoryField.RichestTileDirectionY] = richestTile.dirY;

        ClosestOrganismInfo closest = FindClosestOrganism(self, allOrganisms);
        values[(int)SensoryField.ClosestOrganismDistance] = closest.NormalizedDistance;
        values[(int)SensoryField.ClosestOrganismDirectionX] = closest.DirectionX;
        values[(int)SensoryField.ClosestOrganismDirectionY] = closest.DirectionY;
        values[(int)SensoryField.ClosestOrganismSizeDelta] = closest.NormalizedSizeDelta;
        values[(int)SensoryField.ClosestOrganismRelatedness] = closest.Relatedness;
        values[(int)SensoryField.ClosestOrganismToxicity] = closest.Toxicity;

        NeighborCounts neighbors = CountNeighbors(self, allOrganisms);
        values[(int)SensoryField.NearbySmallerCount] = Math.Tanh(neighbors.Smaller / NearbyCountSaturation);
        values[(int)SensoryField.NearbyLargerCount] = Math.Tanh(neighbors.Larger / NearbyCountSaturation);
        values[(int)SensoryField.LocalDensity] = Math.Tanh(neighbors.Total / NearbyCountSaturation);

        values[(int)SensoryField.LastActionResult] = self.LastActionResult switch
        {
            ActionResult.Success or ActionResult.Killed => 1.0,
            ActionResult.Blocked or ActionResult.Failed => -1.0,
            ActionResult.NoOp => 0.5,
            _ => 0.0, // ActionResult.None
        };

        ReproductionReadiness reproduction = ReproductionRules.Assess(
            self.Genome, self.Energy, self.LastBirthTick, currentTick, _config);
        values[(int)SensoryField.ReproductiveReadiness] = reproduction.IsReady ? 1.0 : 0.0;

        // Global stress level reflects active environmental events, already
        // normalized to [0, 1] by the environment state.
        values[(int)SensoryField.GlobalStressLevel] = globalStress;

        // Diurnal/seasonal cycle + light. Light at the own tile is the clock's global light gated by the
        // biome's light factor; the day/season phases are fed as sin/cos pairs so the brain sees a smooth
        // cyclic signal (no discontinuity at the wrap) and can anticipate, not just react to, the cycle.
        values[(int)SensoryField.LightLevel] = clock.GlobalLight * _terrain.LightFactorAt(self.X, self.Y);
        values[(int)SensoryField.DayPhaseSin] = Math.Sin(2.0 * Math.PI * clock.DayPhase);
        values[(int)SensoryField.DayPhaseCos] = Math.Cos(2.0 * Math.PI * clock.DayPhase);
        values[(int)SensoryField.SeasonPhaseSin] = Math.Sin(2.0 * Math.PI * clock.SeasonPhase);
        values[(int)SensoryField.SeasonPhaseCos] = Math.Cos(2.0 * Math.PI * clock.SeasonPhase);

        (double lightDirX, double lightDirY) = FindBrightestTileDirection(self);
        values[(int)SensoryField.LightDirectionX] = lightDirX;
        values[(int)SensoryField.LightDirectionY] = lightDirY;

        // Sensor cells sharpen perception — a higher effective acuity means less injected noise.
        InjectNoise(values, Morphology.EffectiveAcuity(self.Genome, _config.Multicellular), sensoryNoiseStream);
        return values;
    }

    /// <summary>Gaussian noise scaled by <c>1 - sensory_acuity</c>: pristine at acuity 1, noisiest at acuity 0.</summary>
    private static void InjectNoise(double[] values, double sensoryAcuity, Prng sensoryNoiseStream)
    {
        double stdDev = (1.0 - Math.Clamp(sensoryAcuity, 0.0, 1.0)) * MaxNoiseStdDev;
        if (stdDev <= 0.0)
        {
            return;
        }

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Math.Clamp(values[i] + (stdDev * sensoryNoiseStream.NextGaussian()), -2.0, 2.0);
        }
    }

    private (double Distance, double DirX, double DirY) FindRichestTile(Organism self)
    {
        int radius = (int)Math.Floor(self.Genome.EnvRadius);
        if (radius < 1)
        {
            return (0.0, 0.0, 0.0);
        }

        // Seed the search with the organism's own tile, so "nothing nearby beats home" naturally
        // resolves to distance 0 / no direction, rather than needing a separate found-anything flag.
        int bestX = self.X, bestY = self.Y;
        double bestEnergy = _groundEnergy.EnergyAt(self.X, self.Y);
        double bestDistance = 0.0;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance > radius)
                {
                    continue;
                }

                int x = self.X + dx;
                int y = self.Y + dy;
                if (x < 0 || x >= _world.Width || y < 0 || y >= _world.Height)
                {
                    continue;
                }

                double energy = _groundEnergy.EnergyAt(x, y);

                // Deterministic tie-break: strictly richer wins; ties broken by
                // ascending distance, then ascending (y, x) — never left to iteration order alone.
                bool better = energy > bestEnergy
                    || (energy == bestEnergy
                        && (distance < bestDistance || (distance == bestDistance && (y, x).CompareTo((bestY, bestX)) < 0)));

                if (better)
                {
                    bestEnergy = energy;
                    bestX = x;
                    bestY = y;
                    bestDistance = distance;
                }
            }
        }

        if (bestX == self.X && bestY == self.Y)
        {
            return (0.0, 0.0, 0.0);
        }

        double normalizedDistance = bestDistance / radius;
        double dirLength = Math.Sqrt(((bestX - self.X) * (double)(bestX - self.X)) + ((bestY - self.Y) * (double)(bestY - self.Y)));
        double dirX = dirLength > 0.0 ? (bestX - self.X) / dirLength : 0.0;
        double dirY = dirLength > 0.0 ? (bestY - self.Y) / dirLength : 0.0;
        return (normalizedDistance, dirX, dirY);
    }

    /// <summary>
    /// Unit vector toward the brightest tile within env-radius — the phototaxis gradient. Day/night dims
    /// the whole map uniformly, so the brightest tile is simply the highest-<see cref="Configuration.BiomeSettings.LightFactor"/>
    /// tile in range; if nothing nearby is brighter than home (or the organism can't see), it's (0, 0).
    /// Deterministic tie-break mirrors <see cref="FindRichestTile"/>: brighter wins, then nearer, then (y, x).
    /// </summary>
    private (double DirX, double DirY) FindBrightestTileDirection(Organism self)
    {
        int radius = (int)Math.Floor(self.Genome.EnvRadius);
        if (radius < 1)
        {
            return (0.0, 0.0);
        }

        int bestX = self.X, bestY = self.Y;
        double bestLight = _terrain.LightFactorAt(self.X, self.Y);
        double bestDistance = 0.0;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance > radius)
                {
                    continue;
                }

                int x = self.X + dx;
                int y = self.Y + dy;
                if (x < 0 || x >= _world.Width || y < 0 || y >= _world.Height)
                {
                    continue;
                }

                double light = _terrain.LightFactorAt(x, y);
                bool better = light > bestLight
                    || (light == bestLight
                        && (distance < bestDistance || (distance == bestDistance && (y, x).CompareTo((bestY, bestX)) < 0)));

                if (better)
                {
                    bestLight = light;
                    bestX = x;
                    bestY = y;
                    bestDistance = distance;
                }
            }
        }

        if (bestX == self.X && bestY == self.Y)
        {
            return (0.0, 0.0);
        }

        double dirLength = Math.Sqrt(((bestX - self.X) * (double)(bestX - self.X)) + ((bestY - self.Y) * (double)(bestY - self.Y)));
        double dirX = dirLength > 0.0 ? (bestX - self.X) / dirLength : 0.0;
        double dirY = dirLength > 0.0 ? (bestY - self.Y) / dirLength : 0.0;
        return (dirX, dirY);
    }

    private readonly record struct ClosestOrganismInfo(
        double NormalizedDistance, double DirectionX, double DirectionY, double NormalizedSizeDelta, double Relatedness, double Toxicity);

    private ClosestOrganismInfo FindClosestOrganism(Organism self, IReadOnlyDictionary<long, Organism> allOrganisms)
    {
        int radius = (int)Math.Floor(self.Genome.OrgRadius);
        if (radius < 1)
        {
            return new ClosestOrganismInfo(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
        }

        Organism? best = null;
        double bestDistance = double.PositiveInfinity;

        foreach (Organism other in allOrganisms.Values)
        {
            if (other.Id == self.Id)
            {
                continue;
            }

            double dx = other.X - self.X;
            double dy = other.Y - self.Y;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance > radius)
            {
                continue;
            }

            // Deterministic tie-break: closer wins; equal distance resolves by ascending id.
            if (distance < bestDistance || (distance == bestDistance && best is not null && other.Id < best.Id))
            {
                best = other;
                bestDistance = distance;
            }
        }

        if (best is null)
        {
            return new ClosestOrganismInfo(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
        }

        double dirLength = bestDistance;
        double dirX = dirLength > 0.0 ? (best.X - self.X) / dirLength : 0.0;
        double dirY = dirLength > 0.0 ? (best.Y - self.Y) / dirLength : 0.0;

        double sizeBoundWidth = _config.TraitBounds.Size.Max - _config.TraitBounds.Size.Min;
        double sizeDelta = sizeBoundWidth > 0.0 ? (best.Genome.Size - self.Genome.Size) / sizeBoundWidth : 0.0;
        double relatedness = Kinship.Relatedness(self.Genome, best.Genome, _config.TraitBounds);

        return new ClosestOrganismInfo(bestDistance / radius, dirX, dirY, sizeDelta, relatedness, Math.Clamp(best.Genome.Toxicity, 0.0, 1.0));
    }

    private readonly record struct NeighborCounts(int Smaller, int Larger, int Total);

    private static NeighborCounts CountNeighbors(Organism self, IReadOnlyDictionary<long, Organism> allOrganisms)
    {
        int radius = (int)Math.Floor(self.Genome.OrgRadius);
        if (radius < 1)
        {
            return new NeighborCounts(0, 0, 0);
        }

        int smaller = 0, larger = 0, total = 0;
        foreach (Organism other in allOrganisms.Values)
        {
            if (other.Id == self.Id)
            {
                continue;
            }

            double dx = other.X - self.X;
            double dy = other.Y - self.Y;
            if (Math.Sqrt((dx * dx) + (dy * dy)) > radius)
            {
                continue;
            }

            total++;
            if (other.Genome.Size < self.Genome.Size)
            {
                smaller++;
            }
            else if (other.Genome.Size > self.Genome.Size)
            {
                larger++;
            }
        }

        return new NeighborCounts(smaller, larger, total);
    }
}
