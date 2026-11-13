using System.Net;
using System.Text;
using System.Text.Json;
using NAP.AI;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class GeminiCatalogInsightClientTests
{
    private static CatalogDiversitySnapshot Evidence() => CatalogDiversityReader.Aggregate(new("nimroel"),
        new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["classification:role"] = "carpenter", ["classification:sex"] = "female" },
            new Dictionary<string, string> { ["classification:role"] = "blacksmith", ["classification:sex"] = "male" }
        });

    private static string Outer(string answer) => JsonSerializer.Serialize(new
    {
        status = "completed",
        steps = new[] { new { type = "model_output", content = new[] { new { type = "text", text = answer } } } }
    });

    [Fact]
    public async Task RequestContainsOnlyAggregatesQuestionAndNonPersistentInteraction()
    {
        var snapshot = Evidence(); var first = snapshot.Counts.First().Id;
        using var http = new HttpClient(new FakeHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("generativelanguage.googleapis.com", request.RequestUri!.Host);
            Assert.Equal("/v1/interactions", request.RequestUri.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            Assert.Equal("testing-only", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            var raw = await request.Content!.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement;
            Assert.Equal("gemini-test", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.False(root.GetProperty("stream").GetBoolean());
            Assert.False(root.GetProperty("background").GetBoolean());
            Assert.Equal("none", root.GetProperty("generation_config").GetProperty("tool_choice").GetString());
            using var input = JsonDocument.Parse(root.GetProperty("input").GetString()!);
            Assert.Equal("¿Qué perfiles tenemos?", input.RootElement.GetProperty("question").GetString());
            Assert.Equal("nimroel", input.RootElement.GetProperty("catalog").GetProperty("universe_id").GetString());
            Assert.False(raw.Contains("sqlite", StringComparison.OrdinalIgnoreCase) &&
                raw.Contains("SELECT *", StringComparison.Ordinal));
            Assert.DoesNotContain("api_key", raw, StringComparison.OrdinalIgnoreCase);
            return Ok(Outer(JsonSerializer.Serialize(new { answer = "Necesitamos variedad.", evidence_ids = new[] { first } })));
        }));
        var answer = await new GeminiCatalogInsightClient(http, "testing-only", "gemini-test").AskAsync(snapshot, "¿Qué perfiles tenemos?");
        Assert.Equal("Necesitamos variedad.", answer.Answer);
        Assert.Single(answer.Evidence);
        Assert.Contains(snapshot.Evidence[first], answer.Evidence[0]);
        Assert.False(http.DefaultRequestHeaders.Contains("x-goog-api-key"));
    }

    [Fact]
    public void MissingOrForgedEvidenceIdsAreRejected()
    {
        var snap = Evidence();
        var invalid = new[]
        {
            "{\"answer\":\"Inventado\",\"evidence_ids\":[\"f99999\"]}",
            "{\"answer\":\"\",\"evidence_ids\":[]}",
            "{\"answer\":\"Falta formato\",\"evidence_ids\":\"d00001\"}",
            "{\"answer\":\"Duplicado\",\"evidence_ids\":[\"d00001\",\"d00001\"]}"
        };
        foreach (var raw in invalid)
            Assert.Equal(AiAuditErrorKind.InvalidResponse,
                Assert.Throws<AiAuditClientException>(() => GeminiCatalogInsightClient.ParseResponse(Outer(raw), snap)).Code);
        Assert.Throws<AiAuditClientException>(() => GeminiCatalogInsightClient.ParseResponse("{}", snap));
    }

    [Fact]
    public async Task BadQuestionStopsBeforeNetworkAndNoCredentialsLeakInErrors()
    {
        var messages = 0;
        using var http = new HttpClient(new FakeHandler((_, _) =>
        {
            messages++;
            return Task.FromResult(Ok("{}"));
        }));
        var client = new GeminiCatalogInsightClient(http, "testing-only", "model");
        await Assert.ThrowsAsync<ArgumentException>(() => client.AskAsync(Evidence(), " "));
        await Assert.ThrowsAsync<ArgumentException>(() => client.AskAsync(Evidence(), new string('X', 1001)));
        Assert.Equal(0, messages);
        using var unauthorized = new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            { Content = new StringContent("secret: testing-only", Encoding.UTF8) })));
        var exception = await Assert.ThrowsAsync<AiAuditClientException>(() =>
            new GeminiCatalogInsightClient(unauthorized, "testing-only", "model").AskAsync(Evidence(), "Resumen"));
        Assert.Equal(AiAuditErrorKind.Unauthorized, exception.Code);
        Assert.DoesNotContain("testing-only", exception.Message);
    }

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK)
    { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> run)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            run(request, ct);
    }
}
