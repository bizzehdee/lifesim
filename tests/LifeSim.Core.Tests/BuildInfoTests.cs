using LifeSim.Core;

namespace LifeSim.Core.Tests;

public class BuildInfoTests
{
    [Fact]
    public void SchemaAndConfigVersions_areSemver_1_0()
    {
        Assert.Equal("1.0", BuildInfo.SchemaVersion);
        Assert.Equal("1.0", BuildInfo.ConfigVersion);
    }
}
