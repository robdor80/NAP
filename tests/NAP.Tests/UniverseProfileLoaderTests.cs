using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseProfileLoaderTests
{
    internal static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config", "universes", "nimroel", "profile.json");

    [Fact]
    public void NimroelFile_LoadsExactlyTheHistoricalRule()
    {
        var profile = UniverseProfileLoader.Load(ConfigPath);
        Assert.Equal(new UniverseId("nimroel"), profile.Id);
        Assert.Equal("Nimroel", profile.DisplayName);
        Assert.Equal(new[] { "culture", "realm", "region", "location", "role", "sex" }, profile.ClassificationDimensions);
        var rule = Assert.Single(profile.AssetRules);
        Assert.True(profile.TryGetAssetRule("portrait", "portrait_npc", out var found));
        Assert.Same(rule, found);
        Assert.Equal(profile.ClassificationDimensions, rule.AllowedClassification);
        Assert.Equal(new[] { "culture", "location", "role", "sex" }, rule.RequiredClassification);
        Assert.False(profile.TryGetAssetRule("scene", "scene", out _));
        Assert.True(rule.ValidateClassification(Minimum()).IsValid);
        var missing = Minimum();
        missing.Remove("location");
        Assert.Equal(new[] { "location" }, rule.ValidateClassification(missing).MissingRequired);
        var extra = Minimum();
        extra["faction"] = "anything";
        Assert.Equal(new[] { "faction" }, rule.ValidateClassification(extra).NotAllowed);
        extra.Remove("faction");
        extra["realm"] = "unregistered_value";
        extra["region"] = "unregistered_value";
        Assert.True(rule.ValidateClassification(extra).IsValid);
    }

    [Fact]
    public void StreamLoading_IsGenericAndLeavesStreamOpen()
    {
        const string json = """
            {"schema_version":1,"universe_id":"test_universe","display_name":"Test Universe",
             "classification_dimensions":["dimension"],"asset_rules":[
               {"asset_type":"type","production_profile":"profile","allowed_classification":["dimension"],"required_classification":[]} ]}
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var profile = UniverseProfileLoader.Load(stream);
        Assert.Equal("test_universe", profile.Id.Value);
        Assert.Equal(new[] { "dimension" }, profile.ClassificationDimensions);
        Assert.Single(profile.AssetRules);
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("schema_version")]
    [InlineData("universe_id")]
    [InlineData("display_name")]
    [InlineData("classification_dimensions")]
    [InlineData("asset_rules")]
    public void EveryRootProperty_IsRequired(string property)
    {
        var json = Config();
        json.Remove(property);
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Theory]
    [InlineData("asset_type")]
    [InlineData("production_profile")]
    [InlineData("allowed_classification")]
    [InlineData("required_classification")]
    public void EveryRuleProperty_IsRequired(string property)
    {
        var json = Config();
        Rule(json).Remove(property);
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Theory]
    [InlineData("unknown_root")]
    [InlineData("unknown_rule")]
    [InlineData("null_id")]
    [InlineData("number_id")]
    [InlineData("null_name")]
    [InlineData("null_dimensions")]
    [InlineData("dimensions_object")]
    [InlineData("null_dimension")]
    [InlineData("null_rules")]
    [InlineData("null_rule")]
    [InlineData("null_allowed")]
    [InlineData("null_required")]
    [InlineData("wrong_type")]
    public void StructuralConfigErrors_AreRejected(string mode)
    {
        var json = Config();
        switch (mode)
        {
            case "unknown_root": json["extra"] = true; break;
            case "unknown_rule": Rule(json)["extra"] = true; break;
            case "null_id": json["universe_id"] = null; break;
            case "number_id": json["universe_id"] = 1; break;
            case "null_name": json["display_name"] = null; break;
            case "null_dimensions": json["classification_dimensions"] = null; break;
            case "dimensions_object": json["classification_dimensions"] = new JsonObject(); break;
            case "null_dimension": json["classification_dimensions"]!.AsArray().Add((JsonNode?)null); break;
            case "null_rules": json["asset_rules"] = null; break;
            case "null_rule": json["asset_rules"]!.AsArray().Add((JsonNode?)null); break;
            case "null_allowed": Rule(json)["allowed_classification"] = null; break;
            case "null_required": Rule(json)["required_classification"] = null; break;
            case "wrong_type": Rule(json)["asset_type"] = new JsonArray(); break;
        }
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Theory]
    [InlineData("bad_id")]
    [InlineData("blank_name")]
    [InlineData("bad_dimension")]
    [InlineData("duplicate_dimension")]
    [InlineData("duplicate_allowed")]
    [InlineData("duplicate_required")]
    [InlineData("required_outside_allowed")]
    [InlineData("unregistered")]
    [InlineData("duplicate_combination")]
    public void SemanticConfigErrors_AreRejected(string mode)
    {
        var json = Config();
        switch (mode)
        {
            case "bad_id": json["universe_id"] = " Nimroel "; break;
            case "blank_name": json["display_name"] = "   "; break;
            case "bad_dimension": json["classification_dimensions"]!.AsArray().Add("Upper"); break;
            case "duplicate_dimension": json["classification_dimensions"]!.AsArray().Add("culture"); break;
            case "duplicate_allowed": Rule(json)["allowed_classification"]!.AsArray().Add("culture"); break;
            case "duplicate_required": Rule(json)["required_classification"]!.AsArray().Add("culture"); break;
            case "required_outside_allowed": Rule(json)["required_classification"]!.AsArray().Add("other"); break;
            case "unregistered": Rule(json)["allowed_classification"]!.AsArray().Add("other"); break;
            case "duplicate_combination":
                var copy = Rule(json).DeepClone();
                copy["required_classification"] = new JsonArray();
                json["asset_rules"]!.AsArray().Add(copy);
                break;
        }
        Assert.ThrowsAny<ArgumentException>(() => Load(json));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("null")]
    [InlineData("\"1\"")]
    public void UnsupportedVersion_IsRejected(string version)
    {
        var json = Config();
        json["schema_version"] = JsonNode.Parse(version);
        Assert.Throws<InvalidDataException>(() => Load(json));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1e0")]
    public void MathematicallyIntegerVersionOne_IsAccepted(string version)
    {
        var json = Config();
        json["schema_version"] = JsonNode.Parse(version);
        Assert.Equal(new UniverseId("nimroel"), Load(json).Id);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"schema_version\":1,\"schema_version\":1}")]
    public void MalformedOrDuplicateJson_IsRejected(string json) =>
        Assert.ThrowsAny<JsonException>(() => LoadText(json));

    [Fact]
    public void DuplicateRuleProperty_IsRejected()
    {
        var json = Config().ToJsonString().Replace("\"asset_type\":\"portrait\"", "\"asset_type\":\"portrait\",\"asset_type\":\"portrait\"", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => LoadText(json));
    }

    [Fact]
    public void OperationalAndProgrammingErrors_Propagate()
    {
        Assert.Throws<ArgumentNullException>(() => UniverseProfileLoader.Load((Stream)null!));
        Assert.ThrowsAny<ArgumentException>(() => UniverseProfileLoader.Load("   "));
        Assert.Throws<FileNotFoundException>(() => UniverseProfileLoader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
        using var stream = new FailingReadStream();
        Assert.Throws<IOException>(() => UniverseProfileLoader.Load(stream));
    }

    private static Dictionary<string, string> Minimum() => new()
    { ["culture"] = "anything", ["location"] = "anything", ["role"] = "anything", ["sex"] = "anything" };
    private static JsonObject Config() => JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject();
    private static JsonObject Rule(JsonObject config) => config["asset_rules"]![0]!.AsObject();
    private static UniverseProfile Load(JsonObject json) => LoadText(json.ToJsonString());
    private static UniverseProfile LoadText(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return UniverseProfileLoader.Load(stream);
    }

    private sealed class FailingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Read failure.");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
