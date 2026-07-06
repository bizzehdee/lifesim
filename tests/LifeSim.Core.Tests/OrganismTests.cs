using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class OrganismTests
{
    private static Genome NewGenome() => Genome.MidRange(new TraitBounds());

    private static NeatGenome NewBrain() => NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));

    [Fact]
    public void Constructor_clampsInitialEnergyToTheCeiling()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", Organism.EnergyCeiling + 50.0, 0, 0, NewBrain());
        Assert.Equal(Organism.EnergyCeiling, organism.Energy);
    }

    [Fact]
    public void Constructor_clampsNegativeInitialEnergyToZero()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", -10.0, 0, 0, NewBrain());
        Assert.Equal(0.0, organism.Energy);
        Assert.False(organism.IsAlive);
    }

    [Fact]
    public void Constructor_setsPosition()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", 50.0, 3, 7, NewBrain());
        Assert.Equal(3, organism.X);
        Assert.Equal(7, organism.Y);
    }

    [Fact]
    public void AddEnergy_neverExceedsTheCeiling()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", 90.0, 0, 0, NewBrain());
        organism.AddEnergy(50.0);
        Assert.Equal(Organism.EnergyCeiling, organism.Energy);
    }

    [Fact]
    public void SpendEnergy_clampsAtZero_andReturnsAmountActuallySpent()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", 10.0, 0, 0, NewBrain());
        double spent = organism.SpendEnergy(30.0);

        Assert.Equal(10.0, spent);
        Assert.Equal(0.0, organism.Energy);
        Assert.False(organism.IsAlive);
    }

    [Fact]
    public void Tick_incrementsAge()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", 50.0, 0, 0, NewBrain());
        organism.Tick();
        organism.Tick();
        Assert.Equal(2, organism.Age);
    }

    [Fact]
    public void MoveTo_updatesPosition()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", 50.0, 0, 0, NewBrain());
        organism.MoveTo(5, 9);
        Assert.Equal(5, organism.X);
        Assert.Equal(9, organism.Y);
    }

    [Fact]
    public void RecordAction_setsLastAction()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", 50.0, 0, 0, NewBrain());
        Assert.Null(organism.LastAction);

        organism.RecordAction(OrganismAction.Reproduce);
        Assert.Equal(OrganismAction.Reproduce, organism.LastAction);
    }

    [Fact]
    public void UpdateBrain_replacesTheBrain()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", 50.0, 0, 0, NewBrain());
        NeatGenome newBrain = NewBrain() with { NetworkType = "recurrent-v2" };

        organism.UpdateBrain(newBrain);

        Assert.Equal("recurrent-v2", organism.Brain.NetworkType);
    }

    [Fact]
    public void Factory_resolvesTheDeterministicName()
    {
        var config = new NamingConfig();
        Organism organism = OrganismFactory.Create(42, NewGenome(), config, 50.0, 1, 2, NewBrain());

        Assert.Equal(Naming.OrganismNamer.Name(42, config), organism.Name);
        Assert.Equal(1, organism.X);
        Assert.Equal(2, organism.Y);
    }
}
