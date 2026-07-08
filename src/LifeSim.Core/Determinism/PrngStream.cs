namespace LifeSim.Core.Determinism;

/// <summary>
/// The separable deterministic random streams. Each concern draws from its own
/// stream so that adding a draw in one system never shifts the sequence seen by another.
/// </summary>
public enum PrngStream
{
    /// <summary>World/organism initialization at genesis.</summary>
    Genesis = 0,

    /// <summary>Behaviour selection — softmax action rolls and combat kill rolls.</summary>
    Behavior = 1,

    /// <summary>Trait and NEAT topology mutation.</summary>
    Mutation = 2,

    /// <summary>Stochastic environmental events.</summary>
    Events = 3,

    /// <summary>Gaussian sensory-input noise scaled by acuity.</summary>
    SensoryNoise = 4,

    /// <summary>Sexual-reproduction rolls: the sexual-vs-asexual decision and deterministic mate choice.</summary>
    Mating = 5,
}
