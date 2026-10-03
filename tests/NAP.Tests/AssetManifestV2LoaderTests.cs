using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AssetManifestV2LoaderTests
{
    internal static string ValidJson => """
        {"schema_version":2,"universe_id":"nimroel","asset_id":"portrait_example_001",
         "asset_type":"portrait","production_profile":"portrait_npc","classification":{"role":"farmer"}}
        """;

    [Theory]
    [InlineData("2")]
    [InlineData("2.0")]
    [InlineData("2e0")]
    [InlineData("20e-1")]
    [InlineData("0.2e1")]
    public void StrictLoadRoundtripsAndLeavesCallerStreamOpen(string version)
    {
        var json = Config();
        json["schema_version"] = JsonNode.Parse(version);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var manifest = AssetManifestV2Loader.Load(stream);
        Assert.True(stream.CanRead);
        Assert.Equal(2, manifest.SchemaVersion);
        Assert.Equal("nimroel", manifest.UniverseId);
        Assert.Equal("portrait_example_001", manifest.AssetId);
        Assert.Equal("portrait", manifest.AssetType);
        Assert.Equal("portrait_npc", manifest.ProductionProfile);
        Assert.Equal("farmer", manifest.Classification["role"]);
        var reloaded = Load(JsonSerializer.Serialize(manifest));
        Assert.Equal(manifest.AssetId, reloaded.AssetId);
        Assert.Equal(manifest.Classification, reloaded.Classification);
    }

    [Theory]
    [InlineData("schema_version")]
    [InlineData("universe_id")]
    [InlineData("asset_id")]
    [InlineData("asset_type")]
    [InlineData("production_profile")]
    [InlineData("classification")]
    public void EveryPropertyIsRequiredAndNullIsRejected(string property)
    {
        var json = Config();
        json.Remove(property);
        Assert.Throws<JsonException>(() => Load(json.ToJsonString()));
        json = Config();
        json[property] = null;
        Assert.Throws<JsonException>(() => Load(json.ToJsonString()));
    }

    [Theory]
    [InlineData("schema_version", "\"2\"")]
    [InlineData("universe_id", "1")]
    [InlineData("asset_id", "[]")]
    [InlineData("asset_type", "true")]
    [InlineData("production_profile", "{}")]
    [InlineData("classification", "[]")]
    [InlineData("classification", "\"object\"")]
    public void IncorrectJsonTypesAreRejected(string property, string value)
    {
        var json = Config();
        json[property] = JsonNode.Parse(value);
        Assert.Throws<JsonException>(() => Load(json.ToJsonString()));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("2.5")]
    [InlineData("2.000000000000000000000000000001")]
    [InlineData("1.999999999999999999999999999999")]
    [InlineData("2e999999999999999999999")]
    public void OnlyManifestVersionTwoIsAccepted(string version)
    {
        var json = Config();
        json["schema_version"] = JsonNode.Parse(version);
        Assert.Throws<InvalidDataException>(() => Load(json.ToJsonString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Upper")]
    [InlineData("two words")]
    [InlineData("bad__identifier")]
    [InlineData("identifier_")]
    [InlineData("identifier\n")]
    public void MachineIdentifiersAreRejectedWithoutCorrection(string invalid)
    {
        foreach (var field in new[] { "universe_id", "asset_type", "production_profile" })
        {
            var json = Config();
            json[field] = invalid;
            Assert.Throws<InvalidDataException>(() => Load(json.ToJsonString()));
        }
        var key = Config();
        key["classification"] = new JsonObject { [invalid] = "value" };
        Assert.Throws<InvalidDataException>(() => Load(key.ToJsonString()));
        var value = Config();
        value["classification"]!["role"] = invalid;
        Assert.Throws<InvalidDataException>(() => Load(value.ToJsonString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("portrait_example_000")]
    [InlineData("portrait_001")]
    [InlineData("Portrait_example_001")]
    [InlineData("portrait_example_001 ")]
    [InlineData("portrait_example_001\n")]
    public void InvalidAssetIdentifiersAreDataErrors(string invalid)
    {
        var json = Config();
        json["asset_id"] = invalid;
        Assert.Throws<InvalidDataException>(() => Load(json.ToJsonString()));
    }

    [Fact]
    public void ExactTypePrefixMustMatchAndClassificationMayBeEmpty()
    {
        var json = Config();
        json["asset_type"] = "scene";
        Assert.Throws<InvalidDataException>(() => Load(json.ToJsonString()));
        json["asset_type"] = "portrait_example"; // No descriptor remains after complete type.
        Assert.Throws<InvalidDataException>(() => Load(json.ToJsonString()));
        json["asset_id"] = "audio_master_example_001";
        json["asset_type"] = "audio_master";
        json["classification"] = new JsonObject();
        Assert.Empty(Load(json.ToJsonString()).Classification);
    }

    [Fact]
    public void IdentifierLengthLimitsMatchNaming()
    {
        var json = Config();
        json["universe_id"] = new string('a', 64);
        json["asset_id"] = "p_" + new string('a', 90) + "_001";
        json["asset_type"] = "p";
        json["production_profile"] = new string('a', 64);
        json["classification"] = new JsonObject { [new string('a', 64)] = new string('b', 64) };
        Assert.Equal(96, Load(json.ToJsonString()).AssetId.Length);
        foreach (var field in new[] { "universe_id", "asset_type", "production_profile" })
        {
            var invalid = Config();
            invalid[field] = new string('a', 65);
            Assert.Throws<InvalidDataException>(() => Load(invalid.ToJsonString()));
        }
        json["asset_id"] = "p_" + new string('a', 91) + "_001";
        Assert.Throws<InvalidDataException>(() => Load(json.ToJsonString()));
        var key = Config();
        key["classification"] = new JsonObject { [new string('a', 65)] = "value" };
        Assert.Throws<InvalidDataException>(() => Load(key.ToJsonString()));
        var value = Config();
        value["classification"]!["role"] = new string('a', 65);
        Assert.Throws<InvalidDataException>(() => Load(value.ToJsonString()));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void ClassificationValuesMustBeStrings(string value)
    {
        var json = Config();
        json["classification"]!["role"] = JsonNode.Parse(value);
        Assert.Throws<JsonException>(() => Load(json.ToJsonString()));
    }

    [Theory]
    [InlineData("schema_version", "2")]
    [InlineData("universe_id", "\"nimroel\"")]
    [InlineData("asset_id", "\"portrait_example_001\"")]
    [InlineData("asset_type", "\"portrait\"")]
    [InlineData("production_profile", "\"portrait_npc\"")]
    [InlineData("classification", "{\"role\":\"farmer\"}")]
    [InlineData("role", "\"farmer\"")]
    public void DuplicatePropertiesAreRejected(string property, string value)
    {
        var json = Config().ToJsonString();
        var token = "\"" + property + "\":" + value;
        Assert.Throws<JsonException>(() => Load(json.Replace(token, token + "," + token, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1")]
    public void MalformedOrNonObjectRootIsRejected(string json) => Assert.ThrowsAny<JsonException>(() => Load(json));

    [Fact]
    public void UnknownPropertiesAndCaseChangedPropertyNamesAreRejected()
    {
        var json = Config();
        json["extra"] = true;
        Assert.Throws<JsonException>(() => Load(json.ToJsonString()));
        Assert.Throws<JsonException>(() => Load(ValidJson.Replace("asset_type", "Asset_Type", StringComparison.Ordinal)));
    }

    [Fact]
    public void ProgrammingAndOperationalErrorsPropagate()
    {
        Assert.Throws<ArgumentNullException>(() => AssetManifestV2Loader.Load((Stream)null!));
        Assert.ThrowsAny<ArgumentException>(() => AssetManifestV2Loader.Load(" "));
        using var writeOnly = new MemoryStream();
        writeOnly.Dispose();
        Assert.Throws<ArgumentException>(() => AssetManifestV2Loader.Load(writeOnly));
        using var failing = new FailingReadStream();
        Assert.Throws<IOException>(() => AssetManifestV2Loader.Load(failing));
        var root = Directory.CreateTempSubdirectory("nap-manifest-loader-").FullName;
        try
        {
            var path = Path.Combine(root, "manifest.json");
            Assert.Throws<FileNotFoundException>(() => AssetManifestV2Loader.Load(path));
            File.WriteAllText(path, ValidJson);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal("nimroel", AssetManifestV2Loader.Load(path).UniverseId);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static JsonObject Config() => JsonNode.Parse(ValidJson)!.AsObject();
    private static AssetManifestV2 Load(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return AssetManifestV2Loader.Load(stream);
    }

    private sealed class FailingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Controlled read failure.");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
