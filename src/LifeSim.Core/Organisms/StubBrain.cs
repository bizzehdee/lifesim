using LifeSim.Core.Determinism;

namespace LifeSim.Core.Organisms;

/// <summary>
/// The Phase 4 walking-skeleton decision phase (lifesim.md §7, §17): a uniform random action pick
/// against the behavior PRNG stream, standing in for the NEAT brain + softmax roll (Phase 5, §4)
/// so the tick loop is exercisable end-to-end before the brain exists.
/// </summary>
public static class StubBrain
{
    private const int ActionCount = 11;

    public static OrganismAction SelectAction(Prng behaviorStream) =>
        (OrganismAction)behaviorStream.NextInt(ActionCount);
}
