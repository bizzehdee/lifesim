using LifeSim.Core.Configuration;
using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class OrganismTests
{
    private static Genome NewGenome() => Genome.MidRange(new TraitBounds());

    [Fact]
    public void Constructor_clampsInitialEnergyToTheCeiling()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", Organism.EnergyCeiling + 50.0);
        Assert.Equal(Organism.EnergyCeiling, organism.Energy);
    }

    [Fact]
    public void Constructor_clampsNegativeInitialEnergyToZero()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", -10.0);
        Assert.Equal(0.0, organism.Energy);
        Assert.False(organism.IsAlive);
    }

    [Fact]
    public void AddEnergy_neverExceedsTheCeiling()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", 90.0);
        organism.AddEnergy(50.0);
        Assert.Equal(Organism.EnergyCeiling, organism.Energy);
    }

    [Fact]
    public void SpendEnergy_clampsAtZero_andReturnsAmountActuallySpent()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", 10.0);
        double spent = organism.SpendEnergy(30.0);

        Assert.Equal(10.0, spent);
        Assert.Equal(0.0, organism.Energy);
        Assert.False(organism.IsAlive);
    }

    [Fact]
    public void Tick_incrementsAge()
    {
        var organism = new Organism(1, NewGenome(), "Test-Test-Organism", 50.0);
        organism.Tick();
        organism.Tick();
        Assert.Equal(2, organism.Age);
    }

    [Fact]
    public void Factory_resolvesTheDeterministicName()
    {
        var config = new NamingConfig();
        Organism organism = OrganismFactory.Create(42, NewGenome(), config, 50.0);

        Assert.Equal(Naming.OrganismNamer.Name(42, config), organism.Name);
    }
}
