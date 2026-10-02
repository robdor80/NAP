using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ManifestUniverseScopeTests
{
    [Theory]
    [InlineData("nimroel", true)]
    [InlineData("other_universe", false)]
    public void UniverseBoundary_IsExplicit(string activeId, bool expected)
    {
        var id = new UniverseId(activeId);
        var path = Path.GetTempPath();
        var context = new UniverseContext(new UniverseProfile(id, "Active"), new UniverseStorageConfig(id, path, path, path));
        Assert.Equal(expected, ManifestUniverseScope.Matches(AssetManifestV2Tests.CreateManifest(), context));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Nimroel")]
    [InlineData(" nimroel ")]
    [InlineData("nimroel\n")]
    public void InvalidTransportUniverse_IsRejectedWithoutNormalization(string? value)
    {
        var id = new UniverseId("nimroel");
        var path = Path.GetTempPath();
        var context = new UniverseContext(new UniverseProfile(id, "Active"), new UniverseStorageConfig(id, path, path, path));
        Assert.Throws<ArgumentException>(() => ManifestUniverseScope.Matches(AssetManifestV2Tests.CreateManifest() with { UniverseId = value! }, context));
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ManifestUniverseScope.Matches(null!, null!));
        Assert.Throws<ArgumentNullException>(() => ManifestUniverseScope.Matches(AssetManifestV2Tests.CreateManifest(), null!));
    }
}
