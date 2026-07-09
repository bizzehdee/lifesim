using LifeSim.Core;

namespace LifeSim.Core.Tests;

public class BuildInfoTests
{
    [Fact]
    public void SchemaAndConfigVersions_areCurrent()
    {
        // schema 2.0: sensory input vector widened to 26 for the diurnal/seasonal light senses — a
        // breaking (major) bump, so pre-2.0 snapshots hard-reject on import.
        // config 1.3: environment-cycle block, per-biome light factor, and photosynthesis toggle (additive).
        Assert.Equal("2.0", BuildInfo.SchemaVersion);
        Assert.Equal("1.3", BuildInfo.ConfigVersion);
    }
}
