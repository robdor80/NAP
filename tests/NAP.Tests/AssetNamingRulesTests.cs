using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AssetNamingRulesTests
{
    [Theory]
    [InlineData("portrait")]
    [InlineData("portrait_npc")]
    [InlineData("house_aethros")]
    [InlineData("norgard")]
    [InlineData("future_category_2")]
    public void MachineIdentifier_CanonicalForm_IsValid(string value) =>
        Assert.True(AssetNamingRules.IsValidMachineIdentifier(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Portrait")]
    [InlineData("portrait NPC")]
    [InlineData("portrait-npc")]
    [InlineData("_treskal")]
    [InlineData("treskal_")]
    [InlineData("treskal__north")]
    [InlineData("Norgård")]
    [InlineData("norgård")]
    [InlineData("1portrait")]
    [InlineData("portrait\n")]
    public void MachineIdentifier_InvalidForm_IsRejected(string? value) =>
        Assert.False(AssetNamingRules.IsValidMachineIdentifier(value));

    [Fact]
    public void MachineIdentifier_LengthBoundary_IsEnforced()
    {
        Assert.True(AssetNamingRules.IsValidMachineIdentifier(new string('a', 64)));
        Assert.False(AssetNamingRules.IsValidMachineIdentifier(new string('a', 65)));
    }

    [Theory]
    [InlineData("portrait_treskal_farmer_male_001")]
    [InlineData("portrait_treskal_boy_001")]
    [InlineData("portrait_treskal_farmer_boy_002")]
    [InlineData("scene_treskal_market_dusk_001")]
    [InlineData("heraldry_aethros_banner_001")]
    [InlineData("object_norgard_iron_sword_001")]
    [InlineData("future_type_descriptor_999")]
    public void AssetId_HistoricalAndFutureForms_AreValid(string value) =>
        Assert.True(AssetNamingRules.IsValidAssetId(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Portrait_treskal_farmer_male_001")]
    [InlineData("portrait_treskal_farmer_male_000")]
    [InlineData("portrait_treskal_farmer_male_01")]
    [InlineData("portrait_treskal_farmer_male_1000")]
    [InlineData("portrait_001")]
    [InlineData("portrait__treskal_001")]
    [InlineData("portrait-treskal-farmer-001")]
    [InlineData("portrait treskal farmer 001")]
    [InlineData("portrait_treskál_001")]
    [InlineData("portrait_treskal_001_")]
    [InlineData("portrait_treskal_001\n")]
    public void AssetId_InvalidForm_IsRejected(string? value) =>
        Assert.False(AssetNamingRules.IsValidAssetId(value));

    [Fact]
    public void AssetId_LengthBoundary_IsEnforced()
    {
        Assert.True(AssetNamingRules.IsValidAssetId("portrait_" + new string('a', 83) + "_001"));
        Assert.False(AssetNamingRules.IsValidAssetId("portrait_" + new string('a', 84) + "_001"));
    }

    [Theory]
    [InlineData("portrait_treskal_farmer_male_001", "portrait", true)]
    [InlineData("portrait_treskal_farmer_male_001", "scene", false)]
    [InlineData("portraiture_treskal_001", "portrait", false)]
    [InlineData("future_type_descriptor_001", "future_type", true)]
    [InlineData("future_type_001", "future_type", false)]
    [InlineData("portrait_treskal_000", "portrait", false)]
    [InlineData(null, "portrait", false)]
    [InlineData("portrait_treskal_001", null, false)]
    public void TypeCoherence_RequiresExactPrefixAndDescriptor(string? assetId, string? assetType, bool expected) =>
        Assert.Equal(expected, AssetNamingRules.MatchesAssetType(assetId, assetType));

    [Fact]
    public void Naming_DoesNotInferOrModifyClassification()
    {
        var manifest = new AssetManifestV1
        {
            SchemaVersion = 1,
            AssetId = "portrait_treskal_farmer_male_001",
            AssetType = "portrait",
            ProductionProfile = "portrait_npc",
            Classification = new Dictionary<string, string>
            {
                ["location"] = "hallheim",
                ["role"] = "blacksmith"
            }
        };

        Assert.True(AssetNamingRules.MatchesAssetType(manifest.AssetId, manifest.AssetType));
        _ = new AssetPackageFileNames(manifest.AssetId);

        Assert.Equal("portrait_treskal_farmer_male_001", manifest.AssetId);
        Assert.Equal(2, manifest.Classification.Count);
        Assert.Equal("hallheim", manifest.Classification["location"]);
        Assert.Equal("blacksmith", manifest.Classification["role"]);
    }
}
