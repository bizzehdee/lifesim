namespace LifeSim.Core.Sensing;

/// <summary>
/// Named indices into the fixed sensory input vector. The enum's underlying
/// values are the exact input-node index order fed to the brain — matches
/// <see cref="Neat.NeatTopology.InputNodeIds"/> position-for-position.
/// </summary>
public enum SensoryField
{
    Energy = 0,
    Age = 1,
    TileTemperature = 2,
    BiomeFriction = 3,
    RichestTileDistance = 4,
    RichestTileDirectionX = 5,
    RichestTileDirectionY = 6,
    ClosestOrganismDistance = 7,
    ClosestOrganismDirectionX = 8,
    ClosestOrganismDirectionY = 9,
    ClosestOrganismSizeDelta = 10,
    NearbySmallerCount = 11,
    NearbyLargerCount = 12,
    LocalDensity = 13,
    LastActionResult = 14,
    ReproductiveReadiness = 15,
    GlobalStressLevel = 16,

    /// <summary>Genome relatedness (0..1) to the closest organism — the kin-recognition signal.</summary>
    ClosestOrganismRelatedness = 17,

    /// <summary>
    /// The closest organism's toxicity (0..1) — an honest aposematic warning signal. Lets a predator
    /// evolve to avoid attacking toxic prey rather than only learning by being poisoned.
    /// </summary>
    ClosestOrganismToxicity = 18,

    // --- Diurnal/seasonal cycle + light (see EnvironmentClock, LightFactor). Added as a batch so the
    //     input vector width changes exactly once. ---

    /// <summary>Light at the organism's own tile (0..1): the clock's global light × the biome light factor.</summary>
    LightLevel = 19,

    /// <summary>Sine of the day phase — half of a smooth cyclic encoding of time-of-day (no wrap discontinuity).</summary>
    DayPhaseSin = 20,

    /// <summary>Cosine of the day phase — the other half of the time-of-day encoding; lets a brain anticipate dawn/dusk.</summary>
    DayPhaseCos = 21,

    /// <summary>Sine of the season phase — half of a smooth cyclic encoding of time-of-year.</summary>
    SeasonPhaseSin = 22,

    /// <summary>Cosine of the season phase — the other half; lets a brain anticipate winter/summer.</summary>
    SeasonPhaseCos = 23,

    /// <summary>X of the unit vector toward the brightest tile within env-radius — the phototaxis / shade-seeking gradient.</summary>
    LightDirectionX = 24,

    /// <summary>Y of the phototaxis gradient vector (see <see cref="LightDirectionX"/>).</summary>
    LightDirectionY = 25,
}
