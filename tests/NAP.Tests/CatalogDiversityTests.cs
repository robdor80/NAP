using System.Net;
using System.Text;
using System.Text.Json;
using NAP.AI;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogDiversityTests
{
    [Fact]
    public void FarmerFisherBlacksmithCarpenterExampleFindsRelativeGapWithoutClaimingSurplus()
    {
        var entries = new List<CatalogDiversityEntry>();
        Add("blacksmith", 56); Add("fisher", 45); Add("farmer", 54); Add("carpenter", 2);
        void Add(string role, int count)
        {
            for (int i = 0; i < count; i++)
                entries.Add(Entry($"{role}_{i:000}", role, i % 2 == 0 ? "male" : "female", i % 3 == 0 ? "elder" : "adult"));
        }
        var report = CatalogDiversityAnalyzer.Analyze(new("nimroel"), entries);
        Assert.Equal(157, report.TotalAssets);
        Assert.Equal(157, report.NativeAssets);
        Assert.Equal(0, report.HistoricalAssets);
        Assert.Equal(2, Fact(report, "profesion", "carpenter").Count);
        Assert.Equal(56, Fact(report, "profesion", "blacksmith").Count);
        Assert.Contains(report.Gaps, g => g.Dimension == "profesion" && g.Value == "carpenter" && g.Count == 2 &&
            g.ReferenceValue == "blacksmith" && g.ReferenceCount == 56);
        Assert.DoesNotContain(report.Warnings, x => x.Contains("sobran", StringComparison.OrdinalIgnoreCase));
        using var json = JsonDocument.Parse(report.ToJson());
        Assert.Equal(157, json.RootElement.GetProperty("total_assets").GetInt64());
        Assert.Equal(1, json.RootElement.GetProperty("schema_version").GetInt32());
        Assert.True(json.RootElement.GetProperty("evidence").GetArrayLength() > 4);
    }

    [Fact]
    public void GenderAgeProfessionIntersectionIdentifiesMissingObservedCombinations()
    {
        var rows = Enumerable.Range(1, 9).Select(i => Entry($"blacksmith_{i:000}", "blacksmith", "male", "young"))
            .Concat([Entry("fisher_010", "fisher", "female", "elder"), Entry("fisher_011", "fisher", "female", "elder")]);
        var report = CatalogDiversityAnalyzer.Analyze(new("nimroel"), rows);
        Assert.Equal(0, Fact(report, "profesion × sexo", "blacksmith | female").Count);
        Assert.Equal(0, Fact(report, "profesion × edad", "blacksmith | elder").Count);
        Assert.Equal(9, Fact(report, "profesion × sexo", "blacksmith | male").Count);
        Assert.Equal(2, Fact(report, "sexo", "female").Count);
        Assert.DoesNotContain(report.Evidence, e => e.Dimension == "profesion" && e.Value == "carpenter");
    }

    [Fact]
    public void MissingMetadataIsExplicitAndNotInferredFromPortraitName()
    {
        var asset = Entry("portrait_elder_female_001", "farmer", null, null);
        var report = CatalogDiversityAnalyzer.Analyze(new("nimroel"), [asset]);
        Assert.Equal(1, Fact(report, "sexo", "(sin dato)").Count);
        Assert.Equal(1, Fact(report, "edad", "(sin dato)").Count);
        Assert.Contains(report.Warnings, w => w.Contains("sin metadato de sexo", StringComparison.Ordinal));
        Assert.Contains(report.Warnings, w => w.Contains("sin metadato de edad", StringComparison.Ordinal));
        Assert.Empty(report.Gaps);
    }

    [Fact]
    public void NativeVerifiedAssetSupersedesSameHistoricalIndexIdentity()
    {
        var native = Entry("portrait_farmer_001", "farmer", "male", "adult");
        var historical = native with { Historical = true };
        var report = CatalogDiversityAnalyzer.Analyze(new("nimroel"), [historical, native]);
        Assert.Equal(1, report.TotalAssets);
        Assert.Equal(1, report.NativeAssets); Assert.Equal(0, report.HistoricalAssets);
    }

    [Fact]
    public void HistoricalPortraitsAndOtherAssetTypesAreCountedWithoutFabricatingClassification()
    {
        var historical = Entry("portrait_farmer_001", "farmer", "female", "elder") with { Historical = true };
        var scene = new CatalogDiversityEntry("scene_port_001", "scene", "scene_environment",
            new Dictionary<string, string> { ["culture"] = "norgard", ["location"] = "treskal" }, [], false);
        var report = CatalogDiversityAnalyzer.Analyze(new("nimroel"), [historical, scene]);
        Assert.Equal(1, report.HistoricalAssets);
        Assert.Equal(1, report.NativeAssets);
        Assert.Equal(1, Fact(report, "perfil", "scene_environment").Count);
        Assert.Equal(1, Fact(report, "tipo", "scene").Count);
        Assert.Equal(1, Fact(report, "profesion", "(sin dato)").Count);
    }

    [Fact]
    public void VisualTraitsAreAggregatedByLiteralJsonPointerNotAnInventedAgeTaxonomy()
    {
        var item = Entry("portrait_farmer_001", "farmer", "male", "elder") with
        { Traits = [new("/age/life_stage/value", "elder", CatalogTraitType.String), new("/eyes/color/value", "green", CatalogTraitType.String)] };
        var report = CatalogDiversityAnalyzer.Analyze(new("nimroel"), [item]);
        Assert.Equal(1, Fact(report, "edad", "elder").Count);
        Assert.Equal(1, Fact(report, "rasgo:/eyes/color/value", "green").Count);
    }

    [Fact]
    public void EmptyReportHasNoFictionalGapsAndCanBeSerialized()
    {
        var report = CatalogDiversityAnalyzer.Analyze(new("nimroel"), Array.Empty<CatalogDiversityEntry>());
        Assert.Equal(0, report.TotalAssets); Assert.Empty(report.Evidence); Assert.Empty(report.Gaps);
        Assert.Contains(report.Warnings, w => w.Contains("No hay assets", StringComparison.Ordinal));
        Assert.Contains("\"universe_id\":\"nimroel\"", report.ToJson());
    }

    [Fact]
    public void SQLiteProjectionReadsNativeCatalogWithoutMutatingAnyFile()
    {
        using var f = new CatalogTestFixture();
        var published = f.Publish(identity: "{\"age\":{\"life_stage\":{\"value\":\"adult\"}},\"profession\":\"farmer\"}");
        Assert.True(f.Catalog.RegisterVerified(published.Package, published.Plan, published.Archive, published.Production));
        var before = File.ReadAllBytes(f.Catalog.CatalogPath);
        var sources = f.Sources();
        var report = new CatalogDiversityReader(f.Context).Read();
        Assert.Equal(1, report.NativeAssets);
        Assert.Equal(0, report.HistoricalAssets);
        Assert.Equal(1, Fact(report, "rasgo:/profession", "farmer").Count);
        Assert.Equal(1, Fact(report, "edad", "adult").Count);
        Assert.Equal(before, File.ReadAllBytes(f.Catalog.CatalogPath));
        f.AssertSources(sources);
    }

    [Fact]
    public async Task GeminiOnlyReceivesAggregateAndQuestionAndReturnsVerifiedEvidence()
    {
        var report = Sample();
        string? sent = null;
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("/v1/interactions", request.RequestUri!.AbsolutePath);
            Assert.Equal("TEST_KEY", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            sent = await request.Content!.ReadAsStringAsync();
            using var json = JsonDocument.Parse(sent);
            var root = json.RootElement;
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.False(root.GetProperty("stream").GetBoolean());
            Assert.False(root.GetProperty("background").GetBoolean());
            Assert.Equal("none", root.GetProperty("generation_config").GetProperty("tool_choice").GetString());
            var input = root.GetProperty("input").GetString()!;
            Assert.Contains("carpinteros", input, StringComparison.Ordinal);
            Assert.Contains("e0001", input, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\\\", input, StringComparison.Ordinal);
            return Ok(Outer(Model("{\"summary\":\"Conviene ampliar la variedad de profesiones.\",\"observations\":[{\"comment\":\"Hay menos carpinteros que herreros.\",\"evidence_ids\":[\"e0001\"]}]}")));
        }));
        var answer = await new GeminiCatalogDiversityClient(http, "TEST_KEY", "gemini-test").AskAsync(report, "¿Faltan carpinteros?");
        Assert.Single(answer.Observations);
        Assert.Single(answer.Observations[0].Evidence);
        Assert.Contains(" de ", answer.Observations[0].EvidenceLabel, StringComparison.Ordinal);
        Assert.NotNull(sent);
        Assert.False(http.DefaultRequestHeaders.Contains("x-goog-api-key"));
    }

    [Theory]
    [InlineData("{\"summary\":\"Hay 56 herreros.\",\"observations\":[]}")]
    [InlineData("{\"summary\":\"Faltan tipos.\",\"observations\":[{\"comment\":\"Hay pocos carpinteros.\",\"evidence_ids\":[\"inventado\"]}]}")]
    [InlineData("{\"summary\":\"Faltan tipos.\",\"observations\":[{\"comment\":\"Hay pocos carpinteros.\",\"evidence_ids\":[]}]}")]
    [InlineData("{\"summary\":\"Faltan tipos.\",\"observations\":[],\"sql\":\"DELETE FROM assets\"}")]
    public async Task GeminiResponseCannotInventNumbersIdsOrSQL(string model)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Ok(Outer(Model(model))))));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GeminiCatalogDiversityClient(http, "TEST_KEY", "gemini-test").AskAsync(Sample(), "audita"));
    }

    [Fact]
    public async Task NetworkFailureNeverEchoesCredentials()
    {
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("TEST_KEY secret")));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GeminiCatalogDiversityClient(http, "TEST_KEY", "gemini-test").AskAsync(Sample(), "audita"));
        Assert.DoesNotContain("TEST_KEY", error.Message);
    }

    private static CatalogDiversityEvidence Fact(CatalogDiversityReport report, string dimension, string value) =>
        Assert.Single(report.Evidence, e => e.Dimension == dimension && e.Value == value);

    private static CatalogDiversityEntry Entry(string id, string role, string? sex, string? age)
    {
        var classes = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["culture"] = "norgard", ["location"] = "treskal", ["role"] = role };
        if (sex is not null) classes["sex"] = sex;
        var traits = age is null ? Array.Empty<CatalogVisualTrait>() :
            [new CatalogVisualTrait("/age/life_stage/value", age, CatalogTraitType.String)];
        return new(id, "portrait", "portrait_npc", classes, traits, false);
    }

    private static CatalogDiversityReport Sample() =>
        CatalogDiversityAnalyzer.Analyze(new("nimroel"), [Entry("portrait_carpenter_001", "carpenter", "male", "adult")]);

    private static string Model(string json) => json;
    private static string Outer(string json) => JsonSerializer.Serialize(new
    {
        status = "completed", steps = new[] { new { type = "model_output", content = new[] { new { type = "text", text = json } } } }
    });
    private static HttpResponseMessage Ok(string payload) => new(HttpStatusCode.OK)
    { Content = new StringContent(payload, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            action(request);
    }
}
