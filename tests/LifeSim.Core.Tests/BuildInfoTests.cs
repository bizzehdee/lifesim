using LifeSim.Core;

namespace LifeSim.Core.Tests;

public class BuildInfoTests
{
    [Fact]
    public void SchemaAndConfigVersions_areSemver_1_2()
    {
        // 1.2: multicellular body-plan genome traits + multicellularity config block (lifesim.md §21).
        Assert.Equal("1.2", BuildInfo.SchemaVersion);
        Assert.Equal("1.2", BuildInfo.ConfigVersion);
    }
}
