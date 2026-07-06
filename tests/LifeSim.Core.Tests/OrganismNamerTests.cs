using LifeSim.Core.Configuration;
using LifeSim.Core.Naming;

namespace LifeSim.Core.Tests;

public class OrganismNamerTests
{
    private static readonly NamingConfig Config = new();

    [Fact]
    public void Name_isPureFunctionOfId()
    {
        Assert.Equal(OrganismNamer.Name(12345, Config), OrganismNamer.Name(12345, Config));
    }

    [Fact]
    public void Name_hasThreeHyphenSeparatedParts()
    {
        string name = OrganismNamer.Name(1, Config);
        Assert.Equal(3, name.Split('-').Length);
    }

    [Fact]
    public void DifferentIds_usuallyProduceDifferentNames()
    {
        var names = new HashSet<string>();
        for (long id = 0; id < 50; id++)
        {
            names.Add(OrganismNamer.Name(id, Config));
        }

        Assert.True(names.Count > 40, "Expected most of 50 sequential ids to produce distinct names.");
    }

    [Fact]
    public void RequireDistinctAdjectives_neverRepeatsTheFirstSlot()
    {
        NamingConfig config = Config with { RequireDistinctAdjectives = true };

        for (long id = 0; id < 500; id++)
        {
            string[] parts = OrganismNamer.Name(id, config).Split('-');
            Assert.NotEqual(parts[0], parts[1]);
        }
    }
}
