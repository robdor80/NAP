using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class NimroelRoutingPolicyTests
{
    [Fact]
    public void RealNimroelProfileIsV4AndLoadsItsPortraitRule()
    {
        var json = Read(UniverseProfileLoaderTests.ConfigPath);
        Assert.Equal(4, json["schema_version"]!.GetValue<int>());

        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath);
        Assert.Equal(new UniverseId("nimroel"), profile.Id);
        Assert.Equal(new[] { "culture", "realm", "region", "location", "role", "sex" }, profile.ClassificationDimensions);

        var rule = Assert.Single(profile.AssetRules, r => r.AssetType == "portrait" && r.ProductionProfile == "portrait_npc");
        Assert.Equal("portrait", rule.AssetType);
        Assert.Equal("portrait_npc", rule.ProductionProfile);
        Assert.Equal(profile.ClassificationDimensions, rule.AllowedClassification);
        Assert.Equal(new[] { "culture", "location", "role", "sex" }, rule.RequiredClassification);
    }

    [Fact]
    public void RealNimroelProfilePreservesTheHistoricalV2PackageFilesExactly()
    {
        var current = Assert.Single(UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath).AssetRules, r => r.AssetType == "portrait" && r.ProductionProfile == "portrait_npc");
        var historical = Assert.Single(UniverseProfileLoader.Load(UniverseProfileLoaderTests.HistoricalV2ConfigPath).AssetRules);

        Assert.Equal(
            historical.PackageFiles.Select(FileSignature),
            current.PackageFiles.Select(FileSignature));
    }

    [Fact]
    public void RealNimroelRoutingHasTheSixCanonicalSegmentsInOrder()
    {
        var rule = Assert.Single(UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath).AssetRules, r => r.AssetType == "portrait" && r.ProductionProfile == "portrait_npc");
        var routing = Assert.IsType<AssetRoutingRule>(rule.Routing);

        Assert.Equal(
            new[]
            {
                (AssetRouteSegmentKind.Literal, "portraits"),
                (AssetRouteSegmentKind.Classification, "culture"),
                (AssetRouteSegmentKind.Classification, "location"),
                (AssetRouteSegmentKind.Classification, "role"),
                (AssetRouteSegmentKind.Classification, "sex"),
                (AssetRouteSegmentKind.AssetId, (string?)null)
            },
            routing.Segments.Select(segment => (segment.Kind, segment.Value)));
    }

    [Fact]
    public void CanonicalRoutingExcludesOptionalDimensionsAndUsesOnlyRequiredClassifications()
    {
        var rule = Assert.Single(UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath).AssetRules, r => r.AssetType == "portrait" && r.ProductionProfile == "portrait_npc");
        var dimensions = rule.Routing!.Segments
            .Where(segment => segment.Kind == AssetRouteSegmentKind.Classification)
            .Select(segment => segment.Value!)
            .ToArray();

        Assert.DoesNotContain("realm", dimensions);
        Assert.DoesNotContain("region", dimensions);
        Assert.All(dimensions, dimension => Assert.Contains(dimension, rule.RequiredClassification));
        Assert.Equal(new[] { "culture", "location", "role", "sex" }, dimensions);
    }

    [Fact]
    public void RoutingConfigurationContainsNoCasingOrMappingMechanism()
    {
        var routing = Assert.Single(Read(UniverseProfileLoaderTests.ConfigPath)["asset_rules"]!.AsArray(), r => r!["asset_type"]!.GetValue<string>() == "portrait" && r["production_profile"]!.GetValue<string>() == "portrait_npc")!["routing"]!.AsObject();

        Assert.Equal(new[] { "segments" }, routing.Select(property => property.Key));
        Assert.All(routing["segments"]!.AsArray(), segment =>
            Assert.Contains(Assert.Single(segment!.AsObject()).Key, new[] { "literal", "classification", "asset_id" }));
    }

    [Fact]
    public void HistoricalV2FixtureLoadsWithoutRoutingAndPreservesVersion()
    {
        var json = Read(UniverseProfileLoaderTests.HistoricalV2ConfigPath);
        Assert.Equal(2, json["schema_version"]!.GetValue<int>());
        Assert.Null(Assert.Single(UniverseProfileLoader.Load(UniverseProfileLoaderTests.HistoricalV2ConfigPath).AssetRules).Routing);
    }

    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static object FileSignature(AssetPackageFileRule file) => new
    {
        file.Role,
        file.Suffix,
        file.Extension,
        file.Required,
        file.ContentValidator
    };
}
