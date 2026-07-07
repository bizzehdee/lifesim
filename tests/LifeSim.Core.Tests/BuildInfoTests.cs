using LifeSim.Core;

namespace LifeSim.Core.Tests;

public class BuildInfoTests
{
    [Fact]
    public void SchemaAndConfigVersions_areSemver_1_1()
    {
        // 1.1: share_fraction genome trait + cooperation toggle / senescence knobs (lifesim.md §17, §20).
        Assert.Equal("1.1", BuildInfo.SchemaVersion);
        Assert.Equal("1.1", BuildInfo.ConfigVersion);
    }
}
