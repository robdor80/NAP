using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseAssetKeyTests
{
    [Fact]
    public void SameAssetId_InNimroelAndStarTrek_ProducesDistinctKeys()
    {
        const string assetId = "portrait_example_001";
        var nimroel = new UniverseAssetKey(new UniverseId("nimroel"), assetId);
        var starTrek = new UniverseAssetKey(new UniverseId("star_trek"), assetId);
        Assert.Equal(nimroel.AssetId, starTrek.AssetId);
        Assert.NotEqual(nimroel, starTrek);
        Assert.Equal(2, new HashSet<UniverseAssetKey> { nimroel, starTrek }.Count);
    }

    [Fact]
    public void SameUniverseAndAsset_AreEqualByValue()
    {
        var first = new UniverseAssetKey(new UniverseId("nimroel"), "portrait_example_001");
        var same = new UniverseAssetKey(new UniverseId("nimroel"), "portrait_example_001");
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, new UniverseAssetKey(first.UniverseId, "portrait_example_002"));
    }

    [Fact]
    public void DiagnosticText_DoesNotAlterIdentityOrFilenames()
    {
        var id = new UniverseId("nimroel");
        const string assetId = "portrait_treskal_farmer_male_001";
        var key = new UniverseAssetKey(id, assetId);
        Assert.Equal("nimroel::" + assetId, key.ToString());
        Assert.Same(id, key.UniverseId);
        Assert.Equal(assetId, key.AssetId);
        Assert.Equal(assetId + ".png", new AssetPackageFileNames(key.AssetId).MasterPng);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Portrait_example_001")]
    [InlineData("portrait_example_000")]
    [InlineData("portrait_001")]
    [InlineData("nimroel:portrait_example_001")]
    public void InvalidAssetId_IsRejected(string? assetId) =>
        Assert.Throws<ArgumentException>(() => new UniverseAssetKey(new UniverseId("nimroel"), assetId!));

    [Fact]
    public void NullUniverse_IsRejected() =>
        Assert.Throws<ArgumentNullException>(() => new UniverseAssetKey(null!, "portrait_example_001"));
}
