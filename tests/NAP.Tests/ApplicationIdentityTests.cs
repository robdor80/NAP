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
    public void FullName_IsNimroelAssetPipeline()
    {
        Assert.Equal("Nimroel Asset Pipeline", ApplicationIdentity.FullName);
    }
}
