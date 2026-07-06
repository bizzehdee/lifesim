using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.Sensing;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class SensoryInputBuilderTests
{
    private static readonly WorldState World = new() { Seed = 4242, Width = 40, Height = 40 };
    private static readonly SimulationConfig Config = SimulationConfig.Default;

    private static (SensoryInputBuilder Builder, TerrainSampler Terrain, GroundEnergyGrid Ground) NewBuilder()
    {
        var terrain = new TerrainSampler(World.Seed, Config);
        var ground = new GroundEnergyGrid(terrain, Config);
        return (new SensoryInputBuilder(terrain, ground, Config, World), terrain, ground);
    }

    // SensoryAcuity = 1.0 disables noise injection entirely (stdDev = 0), so exact-value
    // assertions below aren't at the mercy of a Gaussian draw. Only the two acuity-specific tests
    // override this deliberately.
    private static Genome PristineGenome() => Genome.MidRange(Config.TraitBounds) with { SensoryAcuity = 1.0 };

    private static Organism NewOrganism(long id, int x, int y, double energy = 50.0, Genome? genome = null) =>
        new(id, genome ?? PristineGenome(), $"Test-Test-Organism{id}", energy, x, y,
            NeatGenomeFactory.CreateMinimalFullyConnected(new Prng((ulong)id + 1)));

    [Fact]
    public void Build_returnsExactlyOneValuePerSensoryField()
    {
        (SensoryInputBuilder builder, _, _) = NewBuilder();
        Organism self = NewOrganism(1, 20, 20);
        var organisms = new Dictionary<long, Organism> { [self.Id] = self };

        double[] values = builder.Build(self, organisms, 1, new Prng(1));

        Assert.Equal(Enum.GetValues<SensoryField>().Length, values.Length);
        Assert.Equal(NeatTopology.InputCount, values.Length);
    }

    [Fact]
    public void Build_energyAndAge_matchExpectedNormalization()
    {
        (SensoryInputBuilder builder, _, _) = NewBuilder();
        Organism self = NewOrganism(1, 20, 20, energy: 40.0);
        for (int i = 0; i < 7; i++)
        {
            self.Tick();
        }

        var organisms = new Dictionary<long, Organism> { [self.Id] = self };
        double[] values = builder.Build(self, organisms, 1, new Prng(1));

        Assert.Equal(0.4, values[(int)SensoryField.Energy]);
        Assert.Equal(Math.Tanh(7.0 / 100.0), values[(int)SensoryField.Age]);
    }

    [Fact]
    public void Build_tileTemperatureAndFriction_matchTerrainAndConfig()
    {
        (SensoryInputBuilder builder, TerrainSampler terrain, _) = NewBuilder();
        Organism self = NewOrganism(1, 5, 5);
        var organisms = new Dictionary<long, Organism> { [self.Id] = self };

        double[] values = builder.Build(self, organisms, 1, new Prng(1));

        double expectedTemp = terrain.TemperatureAt(5, 5) / 50.0;
        double expectedFriction = Config.Biomes.For(terrain.BiomeAt(5, 5)).Friction / 5.0;
        Assert.Equal(expectedTemp, values[(int)SensoryField.TileTemperature]);
        Assert.Equal(expectedFriction, values[(int)SensoryField.BiomeFriction]);
    }

    [Fact]
    public void Build_richestTile_pointsTowardTheDepositedTile_whenWithinEnvRadius()
    {
        (SensoryInputBuilder builder, _, GroundEnergyGrid ground) = NewBuilder();
        Genome genome = PristineGenome() with { EnvRadius = 5.0 };
        Organism self = NewOrganism(1, 20, 20, genome: genome);

        // Drain everything nearby, then make one specific tile the clear richest.
        for (int dy = -5; dy <= 5; dy++)
        {
            for (int dx = -5; dx <= 5; dx++)
            {
                ground.Drain(20 + dx, 20 + dy, 1000.0);
            }
        }

        ground.Deposit(23, 20, 50.0); // 3 tiles east

        var organisms = new Dictionary<long, Organism> { [self.Id] = self };
        double[] values = builder.Build(self, organisms, 1, new Prng(1));

        Assert.True(values[(int)SensoryField.RichestTileDirectionX] > 0.9);
        Assert.Equal(0.0, values[(int)SensoryField.RichestTileDirectionY], precision: 6);
        Assert.Equal(3.0 / 5.0, values[(int)SensoryField.RichestTileDistance], precision: 6);
    }

    [Fact]
    public void Build_richestTile_isSelfWithZeroDistance_whenNothingNearbyIsRicher()
    {
        (SensoryInputBuilder builder, _, GroundEnergyGrid ground) = NewBuilder();
        Genome genome = PristineGenome() with { EnvRadius = 3.0 };
        Organism self = NewOrganism(1, 20, 20, genome: genome);

        for (int dy = -3; dy <= 3; dy++)
        {
            for (int dx = -3; dx <= 3; dx++)
            {
                if (dx != 0 || dy != 0)
                {
                    ground.Drain(20 + dx, 20 + dy, 1000.0);
                }
            }
        }

        var organisms = new Dictionary<long, Organism> { [self.Id] = self };
        double[] values = builder.Build(self, organisms, 1, new Prng(1));

        Assert.Equal(0.0, values[(int)SensoryField.RichestTileDistance]);
        Assert.Equal(0.0, values[(int)SensoryField.RichestTileDirectionX]);
        Assert.Equal(0.0, values[(int)SensoryField.RichestTileDirectionY]);
    }

    [Fact]
    public void Build_closestOrganism_reportsDistanceDirectionAndSizeDelta()
    {
        (SensoryInputBuilder builder, _, _) = NewBuilder();
        Genome selfGenome = PristineGenome() with { OrgRadius = 10.0, Size = 2.0 };
        Genome otherGenome = PristineGenome() with { Size = 5.0 };

        Organism self = NewOrganism(1, 20, 20, genome: selfGenome);
        Organism other = NewOrganism(2, 20, 24, genome: otherGenome); // 4 tiles south

        var organisms = new Dictionary<long, Organism> { [self.Id] = self, [other.Id] = other };
        double[] values = builder.Build(self, organisms, 1, new Prng(1));

        Assert.Equal(4.0 / 10.0, values[(int)SensoryField.ClosestOrganismDistance], precision: 6);
        Assert.Equal(0.0, values[(int)SensoryField.ClosestOrganismDirectionX], precision: 6);
        Assert.True(values[(int)SensoryField.ClosestOrganismDirectionY] > 0.9);

        double sizeBoundWidth = Config.TraitBounds.Size.Max - Config.TraitBounds.Size.Min;
        Assert.Equal((5.0 - 2.0) / sizeBoundWidth, values[(int)SensoryField.ClosestOrganismSizeDelta], precision: 6);
    }

    [Fact]
    public void Build_closestOrganism_isAbsent_whenNoneWithinOrgRadius()
    {
        (SensoryInputBuilder builder, _, _) = NewBuilder();
        Genome selfGenome = PristineGenome() with { OrgRadius = 2.0 };
        Organism self = NewOrganism(1, 20, 20, genome: selfGenome);
        Organism other = NewOrganism(2, 20, 30); // far outside radius

        var organisms = new Dictionary<long, Organism> { [self.Id] = self, [other.Id] = other };
        double[] values = builder.Build(self, organisms, 1, new Prng(1));

        Assert.Equal(0.0, values[(int)SensoryField.ClosestOrganismDistance]);
        Assert.Equal(0.0, values[(int)SensoryField.ClosestOrganismSizeDelta]);
    }

    [Fact]
    public void Build_countsSmallerAndLargerNeighbors_separately()
    {
        (SensoryInputBuilder builder, _, _) = NewBuilder();
        Genome selfGenome = PristineGenome() with { OrgRadius = 5.0, Size = 5.0 };
        Organism self = NewOrganism(1, 20, 20, genome: selfGenome);

        Organism smaller = NewOrganism(2, 21, 20, genome: PristineGenome() with { Size = 1.0 });
        Organism larger = NewOrganism(3, 20, 21, genome: PristineGenome() with { Size = 9.0 });

        var organisms = new Dictionary<long, Organism> { [self.Id] = self, [smaller.Id] = smaller, [larger.Id] = larger };
        double[] values = builder.Build(self, organisms, 1, new Prng(1));

        Assert.Equal(Math.Tanh(1.0 / 5.0), values[(int)SensoryField.NearbySmallerCount], precision: 6);
        Assert.Equal(Math.Tanh(1.0 / 5.0), values[(int)SensoryField.NearbyLargerCount], precision: 6);
        Assert.Equal(Math.Tanh(2.0 / 5.0), values[(int)SensoryField.LocalDensity], precision: 6);
    }

    [Theory]
    [InlineData(ActionResult.None, 0.0)]
    [InlineData(ActionResult.Success, 1.0)]
    [InlineData(ActionResult.Blocked, -1.0)]
    [InlineData(ActionResult.NoOp, 0.5)]
    public void Build_lastActionResult_mapsToFixedNumericValue(ActionResult result, double expected)
    {
        (SensoryInputBuilder builder, _, _) = NewBuilder();
        Organism self = NewOrganism(1, 20, 20);
        self.RecordActionResult(result);

        var organisms = new Dictionary<long, Organism> { [self.Id] = self };
        double[] values = builder.Build(self, organisms, 1, new Prng(1));

        Assert.Equal(expected, values[(int)SensoryField.LastActionResult]);
    }

    [Fact]
    public void Build_reproductiveReadiness_isBinaryOnEnergyThreshold()
    {
        (SensoryInputBuilder builder, _, _) = NewBuilder();
        Genome genome = PristineGenome() with { Size = 2.0 };
        double cost = Config.Reproduction.ReproductionBaseCost * genome.Size;

        Organism ready = NewOrganism(1, 20, 20, energy: cost + 1.0, genome: genome);
        Organism notReady = NewOrganism(2, 21, 21, energy: cost - 1.0, genome: genome);

        var organisms = new Dictionary<long, Organism> { [ready.Id] = ready, [notReady.Id] = notReady };

        Assert.Equal(1.0, builder.Build(ready, organisms, 1, new Prng(1))[(int)SensoryField.ReproductiveReadiness]);
        Assert.Equal(0.0, builder.Build(notReady, organisms, 1, new Prng(1))[(int)SensoryField.ReproductiveReadiness]);
    }

    [Fact]
    public void Build_globalStressLevel_isZero_untilPhase9Events()
    {
        (SensoryInputBuilder builder, _, _) = NewBuilder();
        Organism self = NewOrganism(1, 20, 20);
        var organisms = new Dictionary<long, Organism> { [self.Id] = self };

        Assert.Equal(0.0, builder.Build(self, organisms, 1, new Prng(1))[(int)SensoryField.GlobalStressLevel]);
    }

    [Fact]
    public void Build_maxAcuity_injectsNoNoise_regardlessOfNoiseStreamState()
    {
        (SensoryInputBuilder builder, _, _) = NewBuilder();
        Genome genome = Genome.MidRange(Config.TraitBounds) with { SensoryAcuity = 1.0 };
        Organism self = NewOrganism(1, 20, 20, genome: genome);
        var organisms = new Dictionary<long, Organism> { [self.Id] = self };

        double[] a = builder.Build(self, organisms, 1, new Prng(111));
        double[] b = builder.Build(self, organisms, 1, new Prng(999));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Build_lowAcuity_measurablyVariesAcrossNoiseDraws()
    {
        (SensoryInputBuilder builder, _, _) = NewBuilder();
        Genome genome = Genome.MidRange(Config.TraitBounds) with { SensoryAcuity = 0.0 };
        Organism self = NewOrganism(1, 20, 20, genome: genome);
        var organisms = new Dictionary<long, Organism> { [self.Id] = self };

        double[] a = builder.Build(self, organisms, 1, new Prng(111));
        double[] b = builder.Build(self, organisms, 1, new Prng(999));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Build_isDeterministic_forIdenticalInputsAndNoiseStreamState()
    {
        (SensoryInputBuilder builder, _, _) = NewBuilder();
        Organism self = NewOrganism(1, 20, 20);
        var organisms = new Dictionary<long, Organism> { [self.Id] = self };

        double[] a = builder.Build(self, organisms, 1, new Prng(555));
        double[] b = builder.Build(self, organisms, 1, new Prng(555));

        Assert.Equal(a, b);
    }
}
