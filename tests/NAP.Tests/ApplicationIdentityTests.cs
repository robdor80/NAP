using NAP.Core;
using Xunit;

namespace NAP.Tests;

public class ApplicationIdentityTests
{
    [Fact]
    public void Name_IsNap()
    {
        Assert.Equal("NAP", ApplicationIdentity.Name);
    }

    [Fact]
    public void FullName_IsNexusAssetPlatform()
    {
        Assert.Equal("Nexus Asset Platform", ApplicationIdentity.FullName);
    }
}
