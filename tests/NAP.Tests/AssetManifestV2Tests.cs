using System.Text.Json;
using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AssetManifestV2Tests
{
    [Fact]
    public void Contract_RoundTripsAllSixFieldsAndGenericClassification()
    {
        var manifest = CreateManifest() with
        {
            UniverseId = "star_trek",
            AssetType = "future_type",
            ProductionProfile = "future_profile",
            Classification = new() { ["faction"] = "example", ["ship"] = "example", ["department"] = "example", ["rank"] = "example" }
        };
        var json = JsonSerializer.Serialize(manifest);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(new[] { "asset_id", "asset_type", "classification", "production_profile", "schema_version", "universe_id" },
            doc.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(2, doc.RootElement.GetProperty("schema_version").GetInt32());
        var restored = Assert.IsType<AssetManifestV2>(JsonSerializer.Deserialize<AssetManifestV2>(json));
        Assert.Equal(manifest.UniverseId, restored.UniverseId);
        Assert.Equal(manifest.AssetId, restored.AssetId);
        Assert.Equal(manifest.AssetType, restored.AssetType);
        Assert.Equal(manifest.ProductionProfile, restored.ProductionProfile);
        Assert.Equal(manifest.Classification, restored.Classification);
    }

    [Theory]
    [InlineData("schema_version")]
    [InlineData("universe_id")]
    [InlineData("asset_id")]
    [InlineData("asset_type")]
    [InlineData("production_profile")]
    [InlineData("classification")]
    public void MissingRootField_IsRejectedByRequiredMembers(string name)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(CreateManifest()))!.AsObject();
        json.Remove(name);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AssetManifestV2>(json.ToJsonString()));
    }

    [Fact]
    public void TransportDto_DoesNotSilentlyNormalizeOrApplyProfileSemantics()
    {
        var manifest = CreateManifest() with { UniverseId = " Nimroel ", Classification = new() { ["Human Key"] = "Value With Spaces" } };
        var restored = Assert.IsType<AssetManifestV2>(JsonSerializer.Deserialize<AssetManifestV2>(JsonSerializer.Serialize(manifest)));
        Assert.Equal(" Nimroel ", restored.UniverseId);
        Assert.Equal("Value With Spaces", restored.Classification["Human Key"]);
        Assert.Empty(CreateManifest().Classification);
    }

    internal static AssetManifestV2 CreateManifest() => new()
    {
        SchemaVersion = 2, UniverseId = "nimroel", AssetId = "portrait_example_001",
        AssetType = "portrait", ProductionProfile = "portrait_npc", Classification = new()
    };
}
