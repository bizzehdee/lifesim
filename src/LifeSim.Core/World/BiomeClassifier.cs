using LifeSim.Core.Configuration;

namespace LifeSim.Core.World;

/// <summary>
/// Pure lookup for the biome matrix (lifesim.md §2): Moisture (Low/High) × Temperature
/// (Cold/Temperate/Hot). Kept independent of noise sampling so the matrix rules are testable
/// without generating a noise field.
/// </summary>
public static class BiomeClassifier
{
    public static Biome Classify(double moisture, double temperature, BiomeThresholds thresholds)
    {
        bool highMoisture = moisture >= thresholds.MoistureHighThreshold;
        bool cold = temperature < thresholds.TemperatureColdThreshold;

        if (highMoisture)
        {
            // High moisture: Cold -> Ice Sheet, Temperate/Hot -> Swamp.
            return cold ? Biome.IceSheet : Biome.Swamp;
        }

        // Low moisture: Cold -> Ice Sheet, Temperate -> Grassland, Hot -> Desert.
        if (cold)
        {
            return Biome.IceSheet;
        }

        bool hot = temperature >= thresholds.TemperatureHotThreshold;
        return hot ? Biome.Desert : Biome.Grassland;
    }
}
