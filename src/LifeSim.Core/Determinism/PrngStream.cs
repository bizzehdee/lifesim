namespace LifeSim.Core.Determinism;

/// <summary>
/// The separable deterministic random streams (lifesim.md §9). Each concern draws from its own
/// stream so that adding a draw in one system never shifts the sequence seen by another.
/// </summary>
public enum PrngStream
{
    /// <summary>World/organism initialization at genesis (lifesim.md §17).</summary>
    Genesis = 0,

    /// <summary>Behaviour selection — softmax action rolls and combat kill rolls (lifesim.md §4, §5).</summary>
    Behavior = 1,

    /// <summary>Trait and NEAT topology mutation (lifesim.md §8).</summary>
    Mutation = 2,

    /// <summary>Stochastic environmental events (lifesim.md §6).</summary>
    Events = 3,

    /// <summary>Gaussian sensory-input noise scaled by acuity (lifesim.md §4, §13).</summary>
    SensoryNoise = 4,
}
