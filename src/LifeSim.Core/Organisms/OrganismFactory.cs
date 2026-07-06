using LifeSim.Core.Configuration;
using LifeSim.Core.Naming;

namespace LifeSim.Core.Organisms;

/// <summary>Constructs an <see cref="Organism"/> with its deterministic name resolved (lifesim.md §19).</summary>
public static class OrganismFactory
{
    public static Organism Create(long id, Genome genome, NamingConfig namingConfig, double initialEnergy) =>
        new(id, genome, OrganismNamer.Name(id, namingConfig), initialEnergy);
}
