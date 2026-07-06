namespace LifeSim.Core.World;

/// <summary>
/// Fractal (fBm) parameters for terrain noise (lifesim.md §2, Appendix A). Shared by every
/// surface — the same C# implementation runs on desktop and under the Avalonia WASM target,
/// so no second-language port is needed (lifesim.md §1).
/// </summary>
public sealed record NoiseConfig
{
    /// <summary>Base spatial frequency (world units per noise period).</summary>
    public double Frequency { get; init; } = 0.01;

    /// <summary>Number of fBm octaves summed.</summary>
    public int Octaves { get; init; } = 4;

    /// <summary>Amplitude falloff per octave (0..1).</summary>
    public double Persistence { get; init; } = 0.5;

    /// <summary>Frequency growth per octave (>1).</summary>
    public double Lacunarity { get; init; } = 2.0;

    /// <summary>The documented default (lifesim.md Appendix A).</summary>
    public static NoiseConfig Default => new();
}
