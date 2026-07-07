using LifeSim.Core.Determinism;

namespace LifeSim.Core.World;

/// <summary>
/// Deterministic 2D simplex noise with a seed-shuffled permutation table.
/// Uses only +, -, *, and floor — no transcendental functions — so results are bit-identical
/// across platforms (desktop and the WASM target), satisfying the terrain-bridge guarantee
///. Terrain is reconstructed from the seed and never stored.
/// </summary>
public sealed class SimplexNoise
{
    // Skew/unskew factors for the 2D simplex grid (exact rationals; no runtime transcendentals).
    private static readonly double F2 = 0.5 * (1.7320508075688772 - 1.0); // (sqrt(3)-1)/2
    private static readonly double G2 = (3.0 - 1.7320508075688772) / 6.0; // (3-sqrt(3))/6

    private static readonly int[][] Grad2 =
    [
        [1, 1], [-1, 1], [1, -1], [-1, -1],
        [1, 0], [-1, 0], [0, 1], [0, -1],
    ];

    private readonly int[] _perm = new int[512];

    public SimplexNoise(ulong seed)
    {
        // Fisher-Yates shuffle of 0..255 using a dedicated PRNG, then duplicated to 512 to
        // avoid index wrapping in the hot path.
        var p = new int[256];
        for (int i = 0; i < 256; i++)
        {
            p[i] = i;
        }

        var rng = new Prng(seed);
        for (int i = 255; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }

        for (int i = 0; i < 512; i++)
        {
            _perm[i] = p[i & 255];
        }
    }

    /// <summary>Single-octave simplex value in roughly [-1, 1].</summary>
    public double Sample(double x, double y)
    {
        double s = (x + y) * F2;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);

        double t = (i + j) * G2;
        double x0 = x - (i - t);
        double y0 = y - (j - t);

        int i1 = x0 > y0 ? 1 : 0;
        int j1 = x0 > y0 ? 0 : 1;

        double x1 = x0 - i1 + G2;
        double y1 = y0 - j1 + G2;
        double x2 = x0 - 1.0 + (2.0 * G2);
        double y2 = y0 - 1.0 + (2.0 * G2);

        int ii = i & 255;
        int jj = j & 255;

        double n0 = Corner(x0, y0, _perm[ii + _perm[jj]]);
        double n1 = Corner(x1, y1, _perm[ii + i1 + _perm[jj + j1]]);
        double n2 = Corner(x2, y2, _perm[ii + 1 + _perm[jj + 1]]);

        // Scale to ~[-1, 1].
        return 70.0 * (n0 + n1 + n2);
    }

    /// <summary>Fractal Brownian motion sum, normalized to roughly [-1, 1].</summary>
    public double SampleFractal(double x, double y, NoiseConfig config)
    {
        double amplitude = 1.0;
        double frequency = config.Frequency;
        double sum = 0.0;
        double maxAmplitude = 0.0;

        for (int octave = 0; octave < config.Octaves; octave++)
        {
            sum += Sample(x * frequency, y * frequency) * amplitude;
            maxAmplitude += amplitude;
            amplitude *= config.Persistence;   // iterative — avoids Math.Pow for determinism
            frequency *= config.Lacunarity;
        }

        return maxAmplitude > 0.0 ? sum / maxAmplitude : 0.0;
    }

    private static double Corner(double x, double y, int gradHash)
    {
        double t = 0.5 - (x * x) - (y * y);
        if (t < 0.0)
        {
            return 0.0;
        }

        int[] g = Grad2[gradHash & 7];
        t *= t;
        return t * t * ((g[0] * x) + (g[1] * y));
    }

    private static int FastFloor(double v)
    {
        int i = (int)v;
        return v < i ? i - 1 : i;
    }
}
