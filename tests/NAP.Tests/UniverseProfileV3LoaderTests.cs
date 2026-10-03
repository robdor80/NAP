using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseProfileV3LoaderTests
{
    internal static string FixturePath => Path.Combine(AppContext.BaseDirectory, "test-data", "phase3", "universe-profile-v3", "profile.json");

    [Theory]
    [InlineData("3")]
    [InlineData("3.0")]
    [InlineData("3e0")]
    public void V3LoadsAllSegmentsAndPackageFilesAndLeavesCallerStreamOpen(string version)
    {
        var json = Config();
        json["schema_version"] = JsonNode.Parse(version);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var profile = UniverseProfileLoader.Load(stream);
        Assert.True(stream.CanRead);
        Assert.Equal("test_universe", profile.Id.Value);
        var rule = Assert.Single(profile.AssetRules);
        Assert.Equal(new[] { "master", "info" }, rule.PackageFiles.Select(file => file.Role));
        Assert.Equal("png_master", rule.PackageFiles[0].ContentValidator);
        Assert.True(rule.PackageFiles[0].Required);
        Assert.Null(rule.PackageFiles[1].ContentValidator);
        Assert.False(rule.PackageFiles[1].Required);
        Assert.Equal(new[] { "culture", "location", "role", "sex" }, rule.RequiredClassification);
        Assert.NotNull(rule.Routing);
        Assert.Equal(new[] { AssetRouteSegmentKind.Literal, AssetRouteSegmentKind.Classification,
            AssetRouteSegmentKind.Classification, AssetRouteSegmentKind.Classification,
            AssetRouteSegmentKind.Classification, AssetRouteSegmentKind.AssetId }, rule.Routing.Segments.Select(segment => segment.Kind));
        Assert.Equal(new string?[] { "portraits", "culture", "location", "role", "sex", null }, rule.Routing.Segments.Select(segment => segment.Value));
    }

    [Fact]
    public void PathLoadDoesNotChangeFixtureBytesOrLastWriteTimeAndIoErrorsPropagate()
    {
        var bytes = File.ReadAllBytes(FixturePath);
        var time = File.GetLastWriteTimeUtc(FixturePath);
        Assert.NotNull(UniverseProfileLoader.Load(FixturePath).AssetRules[0].Routing);
        Assert.Equal(bytes, File.ReadAllBytes(FixturePath));
        Assert.Equal(time, File.GetLastWriteTimeUtc(FixturePath));
        Assert.Throws<FileNotFoundException>(() => UniverseProfileLoader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
    }

    [Theory]
    [InlineData("missing_routing")]
    [InlineData("null_routing")]
    [InlineData("array_routing")]
    [InlineData("unknown_routing")]
    [InlineData("missing_segments")]
    [InlineData("null_segments")]
    [InlineData("object_segments")]
    [InlineData("string_segments")]
    [InlineData("empty_segment")]
    [InlineData("unknown_segment")]
    [InlineData("mixed_segment")]
    [InlineData("null_segment")]
    [InlineData("array_segment")]
    [InlineData("literal_number")]
    [InlineData("literal_null")]
    [InlineData("classification_bool")]
    [InlineData("classification_null")]
    [InlineData("asset_id_false")]
    [InlineData("asset_id_string")]
    [InlineData("asset_id_number")]
    [InlineData("asset_id_null")]
    [InlineData("asset_id_extra")]
    [InlineData("unknown_root")]
    [InlineData("unknown_rule")]
    [InlineData("missing_files")]
    [InlineData("null_files")]
    [InlineData("unknown_file")]
    [InlineData("required_string")]
    public void V3RoutingAndExistingLevelsAreClosedAndStrictlyTyped(string mode)
    {
        var json = Config();
        var rule = Rule(json);
        var routing = rule["routing"]!.AsObject();
        var segments = routing["segments"]!.AsArray();
        switch (mode)
        {
            case "missing_routing": rule.Remove("routing"); break;
            case "null_routing": rule["routing"] = null; break;
            case "array_routing": rule["routing"] = new JsonArray(); break;
            case "unknown_routing": routing["extra"] = true; break;
            case "missing_segments": routing.Remove("segments"); break;
            case "null_segments": routing["segments"] = null; break;
            case "object_segments": routing["segments"] = new JsonObject(); break;
            case "string_segments": routing["segments"] = "assets"; break;
            case "empty_segment": segments[0] = new JsonObject(); break;
            case "unknown_segment": segments[0] = new JsonObject { ["root"] = "assets" }; break;
            case "mixed_segment": segments[0]!["classification"] = "culture"; break;
            case "null_segment": segments[0] = null; break;
            case "array_segment": segments[0] = new JsonArray(); break;
            case "literal_number": segments[0]!["literal"] = 1; break;
            case "literal_null": segments[0]!["literal"] = null; break;
            case "classification_bool": segments[1]!["classification"] = true; break;
            case "classification_null": segments[1]!["classification"] = null; break;
            case "asset_id_false": segments[^1]!["asset_id"] = false; break;
            case "asset_id_string": segments[^1]!["asset_id"] = "true"; break;
            case "asset_id_number": segments[^1]!["asset_id"] = 1; break;
            case "asset_id_null": segments[^1]!["asset_id"] = null; break;
            case "asset_id_extra": segments[^1]!["extra"] = true; break;
            case "unknown_root": json["extra"] = true; break;
            case "unknown_rule": rule["extra"] = true; break;
            case "missing_files": rule.Remove("package_files"); break;
            case "null_files": rule["package_files"] = null; break;
            case "unknown_file": rule["package_files"]![0]!["extra"] = true; break;
            case "required_string": rule["package_files"]![0]!["required"] = "true"; break;
        }
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Theory]
    [InlineData("literal", "portraits")]
    [InlineData("classification", "culture")]
    public void RoutingNamingIsStrictAndUsesExistingLimit(string variant, string valid)
    {
        foreach (var invalid in new string?[] { null, "", " ", "Upper", " Culture ", "a/b", "a\\b", ".", "..", "C:assets", "%HOME%", "{asset_id}", "name\n", new('a', 65) })
        {
            var json = Config();
            Segments(json)[0] = new JsonObject { [variant] = invalid };
            if (invalid is null) Assert.Throws<JsonException>(() => Load(json));
            else Assert.Throws<ArgumentException>(() => Load(json));
        }
        var accepted = Config();
        Segments(accepted)[0] = new JsonObject { [variant] = valid };
        Assert.Equal(valid, Load(accepted).AssetRules[0].Routing!.Segments[0].Value);
    }

    [Theory]
    [InlineData("optional_dimension")]
    [InlineData("unregistered")]
    public void RouteClassificationMustBeRequiredAtLoadTime(string dimension)
    {
        var json = Config();
        Segments(json)[1]!["classification"] = dimension;
        Assert.Throws<ArgumentException>(() => Load(json));
    }

    [Fact]
    public void EmptyRoutingIsInvalidButRepeatedSegmentsAndLiteralOnlyAreAllowed()
    {
        var json = Config();
        Rule(json)["routing"]!["segments"] = new JsonArray();
        Assert.Throws<ArgumentException>(() => Load(json));
        Rule(json)["routing"]!["segments"] = new JsonArray(new JsonObject { ["literal"] = "assets" });
        Assert.Single(Load(json).AssetRules[0].Routing!.Segments);
        Rule(json)["routing"]!["segments"] = new JsonArray(
            new JsonObject { ["asset_id"] = true }, new JsonObject { ["asset_id"] = true });
        Assert.Equal(2, Load(json).AssetRules[0].Routing!.Segments.Count);
        Rule(json)["package_files"] = new JsonArray();
        Assert.Empty(Load(json).AssetRules[0].PackageFiles);
    }

    [Theory]
    [InlineData("schema_version", "3")]
    [InlineData("asset_type", "\"portrait\"")]
    [InlineData("routing", "{}")]
    [InlineData("segments", "[]")]
    [InlineData("literal", "\"portraits\"")]
    [InlineData("classification", "\"culture\"")]
    [InlineData("asset_id", "true")]
    [InlineData("role", "\"master\"")]
    public void DuplicatePropertiesAreRejectedAcrossRootRuleRoutingSegmentAndPackageFile(string property, string duplicateValue)
    {
        var json = Config().ToJsonString();
        var marker = "\"" + property + "\":";
        Assert.Contains(marker, json);
        json = json.Replace(marker, marker + duplicateValue + "," + marker, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => LoadText(json));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void HistoricalVersionsLoadRoutingNullAndRejectRoutingField(int version)
    {
        var path = version == 1 ? UniverseProfileLoaderTests.HistoricalConfigPath : UniverseProfileLoaderTests.HistoricalV2ConfigPath;
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(version, json["schema_version"]!.GetValue<int>());
        Assert.Null(Assert.Single(Load(json).AssetRules).Routing);
        Rule(json)["routing"] = Config()["asset_rules"]![0]!["routing"]!.DeepClone();
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Fact]
    public void V3DoesNotBypassExistingPackageFilesConfigurationGuards()
    {
        var json = Config();
        Rule(json)["package_files"]![1]!["role"] = "master";
        Assert.Throws<ArgumentException>(() => Load(json));
        json = Config();
        Rule(json)["package_files"]![0]!["suffix"] = "_manifest";
        Rule(json)["package_files"]![0]!["extension"] = ".json";
        Assert.Throws<ArgumentException>(() => Load(json));
    }

    [Theory]
    [InlineData("4")]
    [InlineData("3.5")]
    [InlineData("\"3\"")]
    [InlineData("null")]
    public void UnsupportedOrNonNumericVersionsRemainRejected(string version)
    {
        var json = Config();
        json["schema_version"] = JsonNode.Parse(version);
        Assert.Throws<InvalidDataException>(() => Load(json));
    }

    private static JsonObject Config() => JsonNode.Parse(File.ReadAllText(FixturePath))!.AsObject();
    private static JsonObject Rule(JsonObject json) => json["asset_rules"]![0]!.AsObject();
    private static JsonArray Segments(JsonObject json) => Rule(json)["routing"]!["segments"]!.AsArray();
    private static UniverseProfile Load(JsonObject json) => LoadText(json.ToJsonString());
    private static UniverseProfile LoadText(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return UniverseProfileLoader.Load(stream);
    }
}
