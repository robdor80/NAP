using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AssetManifestV1Tests
{
    [Fact]
    public void Deserialize_PhaseOneFixture_PreservesEveryValue()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "test-data", "phase1",
            "first-valid-package", "payload", "portrait_treskal_farmer_male_001_manifest.json");

        var manifest = JsonSerializer.Deserialize<AssetManifestV1>(File.ReadAllText(fixturePath));

        AssertCanonicalManifest(Assert.IsType<AssetManifestV1>(manifest));
    }

    [Fact]
    public void Serialize_CanonicalModel_UsesContractNamesAndRoundTrips()
    {
        var manifest = CreateCanonicalManifest();

        var json = JsonSerializer.Serialize(manifest);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(new[] { "asset_id", "asset_type", "classification", "production_profile", "schema_version" },
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(manifest.AssetId, root.GetProperty("asset_id").GetString());
        Assert.Equal(manifest.AssetType, root.GetProperty("asset_type").GetString());
        Assert.Equal(manifest.ProductionProfile, root.GetProperty("production_profile").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("classification").ValueKind);
        foreach (var (dimension, value) in manifest.Classification)
        {
            Assert.Equal(value, root.GetProperty("classification").GetProperty(dimension).GetString());
        }

        var restored = JsonSerializer.Deserialize<AssetManifestV1>(json);
        AssertCanonicalManifest(Assert.IsType<AssetManifestV1>(restored));
    }

    [Fact]
    public void RoundTrip_FutureCategoryProfileAndDimensions_PreservesData()
    {
        var manifest = CreateCanonicalManifest() with
        {
            AssetType = "new_future_type",
            ProductionProfile = "new_future_profile"
        };
        manifest.Classification.Add("house", "aethros");
        manifest.Classification.Add("faction", "future_faction");

        var restored = Assert.IsType<AssetManifestV1>(
            JsonSerializer.Deserialize<AssetManifestV1>(JsonSerializer.Serialize(manifest)));

        Assert.Equal(manifest.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(manifest.AssetId, restored.AssetId);
        Assert.Equal("new_future_type", restored.AssetType);
        Assert.Equal("new_future_profile", restored.ProductionProfile);
        Assert.Equal(manifest.Classification.Count, restored.Classification.Count);
        foreach (var (dimension, value) in manifest.Classification)
        {
            Assert.Equal(value, restored.Classification[dimension]);
        }
    }

    [Fact]
    public void DeserializeAndRoundTrip_DoesNotNormalizeKeysOrValues()
    {
        const string json = """
            {
              "schema_version": 1,
              "asset_id": " Portrait_Treskal_Farmer_Male_001 ",
              "asset_type": "Portrait",
              "production_profile": " Portrait_NPC ",
              "classification": {
                "culture": "Norgard",
                "location": " Treskal ",
                "role": "Campesino",
                "sex": "Hombre",
                "House": " Aethros "
              }
            }
            """;

        var manifest = Assert.IsType<AssetManifestV1>(JsonSerializer.Deserialize<AssetManifestV1>(json));
        var restored = Assert.IsType<AssetManifestV1>(
            JsonSerializer.Deserialize<AssetManifestV1>(JsonSerializer.Serialize(manifest)));

        foreach (var model in new[] { manifest, restored })
        {
            Assert.Equal(1, model.SchemaVersion);
            Assert.Equal(" Portrait_Treskal_Farmer_Male_001 ", model.AssetId);
            Assert.Equal("Portrait", model.AssetType);
            Assert.Equal(" Portrait_NPC ", model.ProductionProfile);
            Assert.Equal(5, model.Classification.Count);
            Assert.Equal("Norgard", model.Classification["culture"]);
            Assert.Equal(" Treskal ", model.Classification["location"]);
            Assert.Equal("Campesino", model.Classification["role"]);
            Assert.Equal("Hombre", model.Classification["sex"]);
            Assert.Equal(" Aethros ", model.Classification["House"]);
            Assert.False(model.Classification.ContainsKey("house"));
        }
    }

    private static AssetManifestV1 CreateCanonicalManifest() => new()
    {
        SchemaVersion = 1,
        AssetId = "portrait_treskal_farmer_male_001",
        AssetType = "portrait",
        ProductionProfile = "portrait_npc",
        Classification = new Dictionary<string, string>
        {
            ["culture"] = "norgard",
            ["location"] = "treskal",
            ["role"] = "farmer",
            ["sex"] = "male"
        }
    };

    private static void AssertCanonicalManifest(AssetManifestV1 manifest)
    {
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("portrait_treskal_farmer_male_001", manifest.AssetId);
        Assert.Equal("portrait", manifest.AssetType);
        Assert.Equal("portrait_npc", manifest.ProductionProfile);
        Assert.Equal(4, manifest.Classification.Count);
        Assert.Equal("norgard", manifest.Classification["culture"]);
        Assert.Equal("treskal", manifest.Classification["location"]);
        Assert.Equal("farmer", manifest.Classification["role"]);
        Assert.Equal("male", manifest.Classification["sex"]);
    }
}
