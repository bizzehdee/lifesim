using LifeSim.Core.Neat;

namespace LifeSim.App.Presentation;

/// <summary>
/// Display-only cognition index in [0, 100]. The authoritative calculation lives in Core so compact
/// frames can carry it for every organism without transporting every complete network.
/// </summary>
public static class BrainIntelligence
{
    public static double Score(NeatGenome brain) => BrainComplexity.Score(brain);
}
