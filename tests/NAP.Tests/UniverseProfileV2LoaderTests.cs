using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseProfileV2LoaderTests
{
    [Fact]
    public void HistoricalV1LoadsWithEmptyFilesAndRejectsV2Fields()
    {
        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.HistoricalConfigPath);
        Assert.Equal("nimroel", profile.Id.Value);
        Assert.Empty(Assert.Single(profile.AssetRules).PackageFiles);
        var json = JsonNode.Parse(File.ReadAllText(UniverseProfileLoaderTests.HistoricalConfigPath))!.AsObject();
        Rule(json)["package_files"] = new JsonArray();
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Fact]
    public void HistoricalNimroelV2HasExactlyTheRequiredFilesAndKeepsClassification()
    {
        var json = Config();
        Assert.Equal(2, json["schema_version"]!.GetValue<int>());
        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.HistoricalV2ConfigPath);
        Assert.Equal("nimroel", profile.Id.Value);
        Assert.Equal("Nimroel", profile.DisplayName);
        Assert.Equal(new[] { "culture", "realm", "region", "location", "role", "sex" }, profile.ClassificationDimensions);
        Assert.True(profile.TryGetAssetRule("portrait", "portrait_npc", out var rule));
        Assert.Same(Assert.Single(profile.AssetRules), rule);
        Assert.Equal(profile.ClassificationDimensions, rule!.AllowedClassification);
        Assert.Equal(new[] { "culture", "location", "role", "sex" }, rule.RequiredClassification);
        Assert.Equal(new[] { "master", "prompt", "info", "visual_identity" }, rule.PackageFiles.Select(file => file.Role));
        Assert.Equal(new[] { "", "_prompt", "_info", "_visual_identity" }, rule.PackageFiles.Select(file => file.Suffix));
        Assert.Equal(new[] { ".png", ".md", ".md", ".json" }, rule.PackageFiles.Select(file => file.Extension));
        Assert.All(rule.PackageFiles, file => Assert.True(file.Required));
        Assert.Equal(new string?[] { "png_master", null, null, null }, rule.PackageFiles.Select(file => file.ContentValidator));
        const string assetId = "portrait_treskal_farmer_male_001";
        var names = rule.PackageFiles.Select(file => file.ResolveFileName(assetId)).ToArray();
        Assert.Equal(new[] { assetId + ".png", assetId + "_prompt.md", assetId + "_info.md", assetId + "_visual_identity.json" }, names);
        var historical = new AssetPackageFileNames(assetId);
        Assert.Equal(new[] { historical.MasterPng, historical.Prompt, historical.Info, historical.VisualIdentity }, names);
        Assert.Equal(assetId + "_manifest.json", historical.Manifest);
        Assert.DoesNotContain(historical.Manifest, names);
        Assert.DoesNotContain(historical.ProductionWebP, names);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("2.0")]
    [InlineData("2e0")]
    public void GenericV2AllowsEmptyRulesOptionalFilesAndFutureValidators(string version)
    {
        var json = Config();
        json["schema_version"] = JsonNode.Parse(version);
        json["universe_id"] = "test_universe";
        json["display_name"] = "Test Universe";
        var file = PackageFile(json);
        file["role"] = "audio_master";
        file["extension"] = ".wav";
        file["content_validator"] = "future_audio_validator";
        file["required"] = false;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var profile = UniverseProfileLoader.Load(stream);
        Assert.True(stream.CanRead);
        var loaded = profile.AssetRules[0].PackageFiles[0];
        Assert.Equal("audio_master", loaded.Role);
        Assert.Equal("future_audio_validator", loaded.ContentValidator);
        Assert.False(loaded.Required);
        Assert.Equal("portrait_example_001.wav", loaded.ResolveFileName("portrait_example_001"));
        Rule(json)["package_files"] = new JsonArray();
        Assert.Empty(Load(json).AssetRules[0].PackageFiles);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("suffix")]
    [InlineData("extension")]
    [InlineData("required")]
    [InlineData("content_validator")]
    public void EveryPackageFilePropertyIsRequired(string property)
    {
        var json = Config();
        PackageFile(json).Remove(property);
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Theory]
    [InlineData("missing_files")]
    [InlineData("null_files")]
    [InlineData("object_files")]
    [InlineData("null_file")]
    [InlineData("unknown_root")]
    [InlineData("unknown_rule")]
    [InlineData("unknown_file")]
    [InlineData("null_role")]
    [InlineData("null_suffix")]
    [InlineData("null_extension")]
    [InlineData("null_required")]
    [InlineData("string_required")]
    [InlineData("number_required")]
    [InlineData("number_validator")]
    public void V2StructureIsClosedAndStrictlyTyped(string mode)
    {
        var json = Config();
        switch (mode)
        {
            case "missing_files": Rule(json).Remove("package_files"); break;
            case "null_files": Rule(json)["package_files"] = null; break;
            case "object_files": Rule(json)["package_files"] = new JsonObject(); break;
            case "null_file": Rule(json)["package_files"]!.AsArray().Add((JsonNode?)null); break;
            case "unknown_root": json["extra"] = true; break;
            case "unknown_rule": Rule(json)["extra"] = true; break;
            case "unknown_file": PackageFile(json)["extra"] = true; break;
            case "null_role": PackageFile(json)["role"] = null; break;
            case "null_suffix": PackageFile(json)["suffix"] = null; break;
            case "null_extension": PackageFile(json)["extension"] = null; break;
            case "null_required": PackageFile(json)["required"] = null; break;
            case "string_required": PackageFile(json)["required"] = "true"; break;
            case "number_required": PackageFile(json)["required"] = 1; break;
            case "number_validator": PackageFile(json)["content_validator"] = 1; break;
        }
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Theory]
    [InlineData("bad_role")]
    [InlineData("bad_suffix")]
    [InlineData("bad_extension")]
    [InlineData("bad_validator")]
    [InlineData("duplicate_role")]
    [InlineData("filename_collision")]
    [InlineData("universal_manifest")]
    public void V2SemanticConfigurationErrorsAreRejected(string mode)
    {
        var json = Config();
        switch (mode)
        {
            case "bad_role": PackageFile(json)["role"] = " Master "; break;
            case "bad_suffix": PackageFile(json)["suffix"] = "../file"; break;
            case "bad_extension": PackageFile(json)["extension"] = ".PNG"; break;
            case "bad_validator": PackageFile(json)["content_validator"] = "PNG"; break;
            case "duplicate_role": Rule(json)["package_files"]![1]!["role"] = "master"; break;
            case "filename_collision":
                Rule(json)["package_files"]![1]!["suffix"] = "";
                Rule(json)["package_files"]![1]!["extension"] = ".png";
                break;
            case "universal_manifest": PackageFile(json)["suffix"] = "_manifest"; PackageFile(json)["extension"] = ".json"; break;
        }
        Assert.ThrowsAny<ArgumentException>(() => Load(json));
    }

    [Theory]
    [InlineData("schema_version", "2")]
    [InlineData("asset_type", "\"portrait\"")]
    [InlineData("role", "\"master\"")]
    public void DuplicatePropertiesAreRejectedAtEveryLevel(string property, string value)
    {
        var json = Config().ToJsonString();
        var original = "\"" + property + "\":" + value;
        json = json.Replace(original, original + "," + original, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => LoadText(json));
    }

    private static JsonObject Config() => JsonNode.Parse(File.ReadAllText(UniverseProfileLoaderTests.HistoricalV2ConfigPath))!.AsObject();
    private static JsonObject Rule(JsonObject json) => json["asset_rules"]![0]!.AsObject();
    private static JsonObject PackageFile(JsonObject json) => Rule(json)["package_files"]![0]!.AsObject();
    private static UniverseProfile Load(JsonObject json) => LoadText(json.ToJsonString());
    private static UniverseProfile LoadText(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return UniverseProfileLoader.Load(stream);
    }
}
