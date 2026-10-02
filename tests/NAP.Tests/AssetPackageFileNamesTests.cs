using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AssetPackageFileNamesTests
{
    [Fact]
    public void CanonicalNames_AreDerivedExactlyFromAssetId()
    {
        var names = new AssetPackageFileNames("portrait_treskal_farmer_male_001");

        Assert.Equal("portrait_treskal_farmer_male_001", names.AssetId);
        Assert.Equal("portrait_treskal_farmer_male_001.zip", names.Zip);
        Assert.Equal("portrait_treskal_farmer_male_001.png", names.MasterPng);
        Assert.Equal("portrait_treskal_farmer_male_001_prompt.md", names.Prompt);
        Assert.Equal("portrait_treskal_farmer_male_001_info.md", names.Info);
        Assert.Equal("portrait_treskal_farmer_male_001_manifest.json", names.Manifest);
        Assert.Equal("portrait_treskal_farmer_male_001_visual_identity.json", names.VisualIdentity);
        Assert.Equal("portrait_treskal_farmer_male_001.webp", names.ProductionWebP);
    }

    [Theory]
    [InlineData("Portrait_treskal_001")]
    [InlineData(" portrait_treskal_001 ")]
    [InlineData("../portrait_treskal_001")]
    public void InvalidAssetId_IsRejectedRatherThanCorrected(string assetId) =>
        Assert.Throws<ArgumentException>(() => new AssetPackageFileNames(assetId));
}
