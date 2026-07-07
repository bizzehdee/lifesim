using LifeSim.Core.Configuration;
using LifeSim.Core.Naming;
using LifeSim.Core.Neat;

namespace LifeSim.Core.Organisms;

/// <summary>Constructs an <see cref="Organism"/> with its deterministic name resolved.</summary>
public static class OrganismFactory
{
    public static Organism Create(
        long id, Genome genome, NamingConfig namingConfig, double initialEnergy, int x, int y, NeatGenome brain,
        double? energyCapacity = null) =>
        new(id, genome, OrganismNamer.Name(id, namingConfig), initialEnergy, x, y, brain, energyCapacity: energyCapacity);
}
