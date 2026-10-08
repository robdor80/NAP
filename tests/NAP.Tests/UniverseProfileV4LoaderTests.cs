using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseProfileV4LoaderTests
{
    internal static string FixturePath => Path.Combine(AppContext.BaseDirectory, "test-data", "phase5", "universe-profile-v4", "profile.json");

    [Theory]
    [InlineData("4")]
    [InlineData("4.0")]
    [InlineData("4e0")]
    public void V4LoadsGenericObjectAndExplicitNullAndLeavesStreamOpen(string version)
    {
        var json = Config();
        json["schema_version"] = JsonNode.Parse(version);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var profile = UniverseProfileLoader.Load(stream);
        Assert.True(stream.CanRead);
        Assert.Equal("test_universe", profile.Id.Value);
        Assert.Equal(2, profile.AssetRules.Count);
        var conversion = Assert.IsType<ImageConversionRule>(profile.AssetRules[0].Conversion);
        Assert.Equal("image_asset", profile.AssetRules[0].AssetType);
        Assert.Equal(ImageConversionKind.PngToWebp, conversion.Kind);
        Assert.Equal("source_image", conversion.SourceRole);
        Assert.Equal(1200, conversion.OutputWidth);
        Assert.Equal(800, conversion.OutputHeight);
        Assert.Equal(87, conversion.WebpQuality);
        Assert.NotNull(profile.AssetRules[0].Routing);
        Assert.Null(profile.AssetRules[1].Conversion);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("source_role")]
    [InlineData("output_width")]
    [InlineData("output_height")]
    [InlineData("webp_quality")]
    public void MissingOrDuplicateConversionPropertiesAreJsonErrors(string field)
    {
        var json = Config();
        Conversion(json).Remove(field);
        Assert.Throws<JsonException>(() => Load(json));
        var text = Config().ToJsonString();
        var marker = "\"" + field + "\":";
        text = text.Replace(marker, marker + "null," + marker, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => LoadText(text));
    }

    [Theory]
    [InlineData("max_input_pixels")]
    [InlineData("crop")]
    [InlineData("resize_mode")]
    [InlineData("output_extension")]
    [InlineData("fit_mode")]
    [InlineData("path")]
    public void ExtraConversionPropertiesAreRejected(string field)
    {
        var json = Config();
        Conversion(json)[field] = true;
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("\"png_to_webp\"")]
    [InlineData("42")]
    public void ConversionIsRequiredAndMustBeObjectOrExplicitNull(string value)
    {
        var json = Config();
        if (value == "missing") Rule(json).Remove("conversion");
        else Rule(json)["conversion"] = JsonNode.Parse(value);
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Theory]
    [InlineData("kind", "\"unknown\"")]
    [InlineData("kind", "\"PngToWebp\"")]
    [InlineData("kind", "1")]
    [InlineData("kind", "null")]
    [InlineData("source_role", "false")]
    [InlineData("source_role", "null")]
    [InlineData("output_width", "\"1200\"")]
    [InlineData("output_width", "null")]
    [InlineData("output_width", "1.5")]
    [InlineData("output_height", "true")]
    [InlineData("output_height", "[]")]
    [InlineData("webp_quality", "\"87\"")]
    [InlineData("webp_quality", "{}")]
    public void WrongTypesAndUnknownKindAreJsonErrors(string field, string value)
    {
        var json = Config();
        Conversion(json)[field] = JsonNode.Parse(value);
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Theory]
    [InlineData("output_width", 0, "outputWidth")]
    [InlineData("output_width", 16384, "outputWidth")]
    [InlineData("output_height", 0, "outputHeight")]
    [InlineData("output_height", 16384, "outputHeight")]
    [InlineData("webp_quality", -1, "webpQuality")]
    [InlineData("webp_quality", 101, "webpQuality")]
    public void NumericRangesAreDefendedByRule(string field, int value, string parameter)
    {
        var json = Config();
        Conversion(json)[field] = value;
        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() => Load(json)).ParamName);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("required")]
    [InlineData("extension")]
    [InlineData("validator_null")]
    [InlineData("validator_other")]
    public void SourceFileInvariantIsEnforcedAtProfileLoad(string mode)
    {
        var json = Config();
        var file = Rule(json)["package_files"]![0]!.AsObject();
        switch (mode)
        {
            case "role": Conversion(json)["source_role"] = "missing"; break;
            case "required": file["required"] = false; break;
            case "extension": file["extension"] = ".jpg"; break;
            case "validator_null": file["content_validator"] = null; break;
            default: file["content_validator"] = "other"; break;
        }
        Assert.Equal("conversion", Assert.Throws<ArgumentException>(() => Load(json)).ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void HistoricalProfilesLoadWithoutConversionAndRejectV4Fields(int version)
    {
        var path = version switch
        {
            1 => UniverseProfileLoaderTests.HistoricalConfigPath,
            2 => UniverseProfileLoaderTests.HistoricalV2ConfigPath,
            _ => UniverseProfileV3LoaderTests.FixturePath
        };
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(version, json["schema_version"]!.GetValue<int>());
        Assert.All(Load(json).AssetRules, rule => Assert.Null(rule.Conversion));
        Rule(json)["conversion"] = null;
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Fact]
    public void VersionsDoNotSilentlyReinterpretConversion()
    {
        var json = Config();
        json["schema_version"] = 3;
        Assert.Throws<JsonException>(() => Load(json));
        json = JsonNode.Parse(File.ReadAllText(UniverseProfileV3LoaderTests.FixturePath))!.AsObject();
        json["schema_version"] = 4;
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Fact]
    public void RealNimroelPreservesPortraitV4ConversionClassificationFilesAndRouting()
    {
        var json = JsonNode.Parse(File.ReadAllText(UniverseProfileLoaderTests.ConfigPath))!.AsObject();
        Assert.Equal(4, json["schema_version"]!.GetValue<int>());
        var profile = Load(json);
        Assert.Equal("nimroel", profile.Id.Value);
        Assert.Equal("Nimroel", profile.DisplayName);
        var rule = Assert.Single(profile.AssetRules, r => r.AssetType == "portrait" && r.ProductionProfile == "portrait_npc");
        Assert.Equal("portrait", rule.AssetType);
        Assert.Equal("portrait_npc", rule.ProductionProfile);
        Assert.Equal(new[] { "culture", "realm", "region", "location", "role", "sex" }, profile.ClassificationDimensions);
        Assert.Equal(profile.ClassificationDimensions, rule.AllowedClassification);
        Assert.Equal(new[] { "culture", "location", "role", "sex" }, rule.RequiredClassification);
        Assert.Equal(new[] { "master", "prompt", "info", "visual_identity" }, rule.PackageFiles.Select(f => f.Role));
        Assert.Equal(new[] { "", "_prompt", "_info", "_visual_identity" }, rule.PackageFiles.Select(f => f.Suffix));
        Assert.Equal(new[] { ".png", ".md", ".md", ".json" }, rule.PackageFiles.Select(f => f.Extension));
        Assert.All(rule.PackageFiles, f => Assert.True(f.Required));
        Assert.Equal(new string?[] { "png_master", null, null, null }, rule.PackageFiles.Select(f => f.ContentValidator));
        Assert.Equal(new string?[] { "portraits", "culture", "location", "role", "sex", null }, rule.Routing!.Segments.Select(s => s.Value));
        Assert.Equal(new ImageConversionRule(ImageConversionKind.PngToWebp, "master", 768, 960, 90), rule.Conversion);
        Assert.DoesNotContain("max_input_pixels", json.ToJsonString());
        Assert.Contains(profile.AssetRules, r => r.AssetType == "scene" && r.ProductionProfile == "scene_cartography");
    }

    private static JsonObject Config() => JsonNode.Parse(File.ReadAllText(FixturePath))!.AsObject();
    private static JsonObject Rule(JsonObject json) => json["asset_rules"]![0]!.AsObject();
    private static JsonObject Conversion(JsonObject json) => Rule(json)["conversion"]!.AsObject();
    private static UniverseProfile Load(JsonObject json) => LoadText(json.ToJsonString());
    private static UniverseProfile LoadText(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return UniverseProfileLoader.Load(stream);
    }
}
