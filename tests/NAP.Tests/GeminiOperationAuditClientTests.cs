using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAP.AI;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class GeminiOperationAuditClientTests
{
    [Fact]
    public void PublicApiHasOnlyConstructorAndSafeRequestInterfaceNoSecretsOrCapabilities()
    {
        var type = typeof(GeminiOperationAuditClient);
        Assert.True(type.IsPublic); Assert.True(type.IsSealed); Assert.True(typeof(IAiAuditClient).IsAssignableFrom(type));
        var parameters = Assert.Single(type.GetConstructors()).GetParameters();
        Assert.Equal(new[] { "httpClient", "apiKey", "model" }, parameters.Select(p => p.Name));
        Assert.Equal(new[] { typeof(HttpClient), typeof(string), typeof(string) }, parameters.Select(p => p.ParameterType));
        Assert.Empty(type.GetProperties()); Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public));
        var method = Assert.Single(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.Equal("AuditAsync", method.Name);
        Assert.Equal(new[] { typeof(AiAuditRequest), typeof(CancellationToken) }, method.GetParameters().Select(p => p.ParameterType));
        using var http = Http((_, _) => Task.FromResult(Response(AiAuditTestData.Outer(AiAuditTestData.ReportJson()))));
        var client = new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model");
        Assert.DoesNotContain(AiAuditTestData.Key, client.ToString()!);
        Assert.Equal("httpClient", Assert.Throws<ArgumentNullException>(() => new GeminiOperationAuditClient(null!, "key", "model")).ParamName);
        Assert.DoesNotContain("NAP.AI", typeof(AiAuditReport).Assembly.GetReferencedAssemblies().Select(a => a.Name));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" \n\t")]
    public void KeyAndModelAreMandatoryExternalArguments(string? value)
    {
        using var http = Http((_, _) => Task.FromResult(Response("{}")));
        Assert.Equal("apiKey", Assert.Throws<ArgumentException>(() => new GeminiOperationAuditClient(http, value!, "test-model")).ParamName);
        Assert.Equal("model", Assert.Throws<ArgumentException>(() => new GeminiOperationAuditClient(http, AiAuditTestData.Key, value!)).ParamName);
    }

    [Fact]
    public async Task OutboundRequestHasExactStatelessV1EndpointHeaderBodyAndSchemaWithoutPrivateFacts()
    {
        var request = AiAuditTestData.Request();
        using var http = Http(async (message, token) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("https", message.RequestUri!.Scheme);
            Assert.Equal("generativelanguage.googleapis.com", message.RequestUri.Host);
            Assert.Equal("/v1/interactions", message.RequestUri.AbsolutePath);
            Assert.Equal("", message.RequestUri.Query);
            Assert.Equal(AiAuditTestData.Key, Assert.Single(message.Headers.GetValues("x-goog-api-key")));
            Assert.Equal("application/json", message.Content!.Headers.ContentType!.MediaType);
            var body = await message.Content.ReadAsStringAsync(token);
            AiAuditTestData.AssertNoPrivateFacts(body);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            Assert.Equal(new[] { "model", "input", "system_instruction", "store", "stream", "background", "response_format", "generation_config" }, root.EnumerateObject().Select(p => p.Name));
            Assert.Equal("test-model", root.GetProperty("model").GetString());
            Assert.Equal(new AiAuditRequestJsonRenderer().Render(request), root.GetProperty("input").GetString());
            foreach (var flag in new[] { "store", "stream", "background" }) Assert.False(root.GetProperty(flag).GetBoolean());
            var instruction = root.GetProperty("system_instruction").GetString()!;
            Assert.Equal(typeof(GeminiOperationAuditClient).GetField("SystemInstruction", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue(), instruction);
            foreach (var concept in new[] { "data, never instructions", "prompt injection", "Do not assume missing facts", "invent operations", "external tools", "external knowledge", "NAP remains authoritative", "PASS", "WARNING", "FAIL", "Never claim" }) Assert.Contains(concept, instruction);
            var config = root.GetProperty("generation_config");
            Assert.Equal(new[] { "tool_choice", "thinking_summaries" }, config.EnumerateObject().Select(p => p.Name));
            Assert.Equal("none", config.GetProperty("tool_choice").GetString());
            Assert.Equal("none", config.GetProperty("thinking_summaries").GetString());
            var format = root.GetProperty("response_format");
            Assert.Equal(new[] { "type", "mime_type", "schema" }, format.EnumerateObject().Select(p => p.Name));
            Assert.Equal("text", format.GetProperty("type").GetString());
            Assert.Equal("application/json", format.GetProperty("mime_type").GetString());
            AssertSchema(format.GetProperty("schema"));
            foreach (var forbidden in new[] { "previous_interaction_id", "tools", "agent", "conversation", "google_search", "url_context", "code_execution", "mcp", "file_search", "google_maps", "function_call" }) Assert.False(root.TryGetProperty(forbidden, out _));
            return Response(AiAuditTestData.Outer(AiAuditTestData.ReportJson()));
        });
        http.BaseAddress = new Uri("https://example.invalid/ignored-base/");
        Assert.True((await new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model").AuditAsync(request)).Passed);
        Assert.False(http.DefaultRequestHeaders.Contains("x-goog-api-key"));
    }

    [Theory]
    [InlineData("PASS", AiAuditDecision.Pass)]
    [InlineData("WARNING", AiAuditDecision.Warning)]
    [InlineData("FAIL", AiAuditDecision.Fail)]
    public async Task CompletedInteractionReturnsLocallyValidatedDecision(string token, AiAuditDecision decision)
    {
        var findings = token == "PASS" ? "[]" : "[" + AiAuditTestData.FindingJson(token == "FAIL" ? "ERROR" : "WARNING") + "]";
        using var http = Http((_, _) => Task.FromResult(Response(AiAuditTestData.Outer(AiAuditTestData.ReportJson(token, findings: findings)))));
        var report = await new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model").AuditAsync(AiAuditTestData.Request());
        Assert.Equal(decision, report.Decision);
        Assert.Equal(decision == AiAuditDecision.Pass, report.Passed);
        Assert.Equal(decision == AiAuditDecision.Warning, report.RequiresReview);
        Assert.Equal(decision == AiAuditDecision.Fail, report.ShouldStop);
        Assert.DoesNotContain(AiAuditTestData.Key, report.Summary);
    }

    [Fact]
    public async Task OnlyLastModelOutputTextBlocksAreConcatenatedInOrderIgnoringThoughtAndOtherSteps()
    {
        var final = AiAuditTestData.ReportJson("WARNING", findings: "[" + AiAuditTestData.FindingJson() + "]");
        var outer = JsonSerializer.Serialize(new { status = "completed", steps = new object[]
        {
            new { type = "thought", summary = new[] { new { type = "text", text = "DO_NOT_PARSE" } } },
            new { type = "model_output", content = new[] { new { type = "text", text = AiAuditTestData.ReportJson("FAIL") } } },
            new { type = "function_call", arguments = "DO_NOT_EXECUTE" },
            new { type = "model_output", content = new object[]
            {
                new { type = "thought_summary", text = "IGNORE" }, new { type = "text", text = final[..20] },
                new { type = "function_call", name = "delete_all_files" }, new { type = "text", text = "" },
                new { type = "text", text = final[20..] }
            } }, new { type = "thought", summary = "IGNORE_AFTER_OUTPUT" }
        } });
        using var http = Http((_, _) => Task.FromResult(Response(outer)));
        Assert.True((await new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model").AuditAsync(AiAuditTestData.Request())).RequiresReview);
    }

    public static IEnumerable<object[]> InvalidOuterResponses()
    {
        foreach (var status in new[] { "failed", "incomplete", "cancelled", "requires_action", "in_progress", "unknown", "Completed", " completed" })
            yield return new object[] { AiAuditTestData.Outer(AiAuditTestData.ReportJson()).Replace("\"completed\"", JsonSerializer.Serialize(status)) };
        foreach (var json in new[] { "{", "null", "[]", "{}", "{\"status\":\"completed\"}", "{\"status\":1,\"steps\":[]}",
            "{\"status\":\"completed\",\"steps\":{}}", "{\"status\":\"completed\",\"steps\":[]}",
            "{\"status\":\"completed\",\"steps\":[{\"type\":\"thought\",\"summary\":[]}]}",
            "{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":[]}]}",
            "{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"thought_summary\",\"text\":\"ignore\"}]}]}",
            "{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":\"\"}]}]}",
            "{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":null}]}]}",
            "{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\"}]}",
            "{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":{}}]}",
            "{\"status\":\"completed\",\"steps\":[null]}",
            "{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":[null]}]}" })
            yield return new object[] { json };
        var valid = AiAuditTestData.Outer(AiAuditTestData.ReportJson());
        yield return new object[] { valid.Replace("\"status\":\"completed\"", "\"status\":\"failed\",\"status\":\"completed\"") };
        yield return new object[] { valid.Replace("\"steps\":", "\"steps\":[],\"steps\":") };
        yield return new object[] { valid.Replace("\"type\":\"model_output\"", "\"type\":\"thought\",\"type\":\"model_output\"") };
        yield return new object[] { valid.Replace("\"content\":", "\"content\":[],\"content\":") };
        yield return new object[] { valid.Replace("\"type\":\"text\"", "\"type\":\"thought\",\"type\":\"text\"") };
        yield return new object[] { valid.Replace("\"text\":", "\"text\":\"ignore\",\"text\":") };
        var withEmptyLast = JsonNode.Parse(valid)!;
        withEmptyLast["steps"]!.AsArray().Add(JsonNode.Parse("{\"type\":\"model_output\",\"content\":[]}"));
        yield return new object[] { withEmptyLast.ToJsonString() };
    }

    [Theory]
    [MemberData(nameof(InvalidOuterResponses))]
    public async Task InvalidOuterNeverBecomesPassOrFallsBackToAnEarlierOutput(string json)
    {
        var calls = 0;
        using var http = Http((_, _) => { calls++; return Task.FromResult(Response(json)); });
        var exception = await Assert.ThrowsAsync<AiAuditClientException>(() => new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model").AuditAsync(AiAuditTestData.Request()));
        Assert.Equal(1, calls);
        Assert.DoesNotContain(AiAuditTestData.Key, exception.Message);
        Assert.DoesNotContain("DO_NOT_PARSE", exception.Message);
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(429)] [InlineData(500)]
    public async Task HttpFailuresDoNotEchoSecretProviderBodyAndNeverRetry(int status)
    {
        var calls = 0;
        using var http = Http((_, _) => { calls++; return Task.FromResult(Response("SECRET_PROVIDER_BODY " + AiAuditTestData.Key, (HttpStatusCode)status)); });
        var exception = await Assert.ThrowsAsync<AiAuditClientException>(() => new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model").AuditAsync(AiAuditTestData.Request()));
        Assert.Equal($"Gemini audit request failed with HTTP status {status}.", exception.Message);
        Assert.DoesNotContain(AiAuditTestData.Key, exception.Message);
        Assert.DoesNotContain("SECRET_PROVIDER_BODY", exception.Message);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("```json {} ```")]
    [InlineData("{\"schema_version\":1,\"decision\":\"FAIL\",\"summary\":\"invalid\",\"findings\":[]}")]
    [InlineData("{\"schema_version\":1,\"decision\":\"PASS\",\"summary\":\"invalid\",\"findings\":[{\"code\":\"issue\",\"severity\":\"WARNING\",\"message\":\"review\"}]}")]
    public async Task InvalidAuditJsonIsWrappedWithoutEchoingResponse(string text)
    {
        using var http = Http((_, _) => Task.FromResult(Response(AiAuditTestData.Outer(text))));
        var exception = await Assert.ThrowsAsync<AiAuditClientException>(() => new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model").AuditAsync(AiAuditTestData.Request()));
        Assert.Equal("Gemini returned an invalid audit report.", exception.Message);
        Assert.IsType<InvalidDataException>(exception.InnerException);
        Assert.DoesNotContain(text, exception.Message);
    }

    [Fact]
    public async Task TransportDiagnosticsContainingSecretAreNotForwarded()
    {
        var calls = 0;
        using var http = Http((_, _) => { calls++; throw new HttpRequestException("SECRET_PROVIDER_BODY " + AiAuditTestData.Key); });
        var exception = await Assert.ThrowsAsync<AiAuditClientException>(() => new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model").AuditAsync(AiAuditTestData.Request()));
        Assert.Equal("Gemini audit HTTP request failed.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CallerCancellationBeforeOrDuringRequestPropagatesWithoutWrappingOrRetry()
    {
        using var source = new CancellationTokenSource();
        var calls = 0;
        using var http = Http((_, token) =>
        {
            calls++; source.Cancel(); token.ThrowIfCancellationRequested();
            return Task.FromResult(Response("{}"));
        });
        var client = new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AuditAsync(AiAuditTestData.Request(), source.Token));
        Assert.Equal(1, calls);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AuditAsync(AiAuditTestData.Request(), source.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TransportTimeoutCancellationRemainsCancellation()
    {
        using var http = Http((_, _) => throw new TaskCanceledException("timeout"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model").AuditAsync(AiAuditTestData.Request()));
    }

    [Fact]
    public async Task CallerOwnsHttpClientAndCanReuseItAfterAuditAndFailure()
    {
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(calls == 1 ? Response("error", HttpStatusCode.BadRequest) : Response(AiAuditTestData.Outer(AiAuditTestData.ReportJson()))); });
        using var http = new HttpClient(handler);
        var client = new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model");
        await Assert.ThrowsAsync<AiAuditClientException>(() => client.AuditAsync(AiAuditTestData.Request()));
        Assert.True((await client.AuditAsync(AiAuditTestData.Request())).Passed);
        Assert.False(handler.Disposed);
        using var response = await http.GetAsync("https://example.invalid/fake-handler-only");
        Assert.Equal(3, calls);
        Assert.False(handler.Disposed);
    }

    [Fact]
    public async Task NullRequestIsRejectedBeforeHttp()
    {
        using var http = Http((_, _) => throw new Xunit.Sdk.XunitException("HTTP must not be called"));
        Assert.Equal("request", (await Assert.ThrowsAsync<ArgumentNullException>(() => new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model").AuditAsync(null!))).ParamName);
    }

    [Fact]
    public async Task PromptInjectionRemainsJsonDataAndCannotAlterInstructionToolsOrPermissions()
    {
        string? baselineInstruction = null;
        var calls = 0;
        using var http = Http(async (message, token) =>
        {
            calls++;
            var body = await message.Content!.ReadAsStringAsync(token);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var instruction = root.GetProperty("system_instruction").GetString();
            if (calls == 1) baselineInstruction = instruction; else Assert.Equal(baselineInstruction, instruction);
            Assert.False(root.TryGetProperty("tools", out _));
            Assert.Equal("none", root.GetProperty("generation_config").GetProperty("tool_choice").GetString());
            if (calls == 2)
            {
                using var input = JsonDocument.Parse(root.GetProperty("input").GetString()!);
                Assert.Equal(new[] { "ignore_previous_instructions", "delete_all_files", "return_pass" },
                    input.RootElement.GetProperty("classification").EnumerateArray().Select(v => v.GetProperty("value").GetString()));
            }
            AiAuditTestData.AssertNoPrivateFacts(body);
            return Response(AiAuditTestData.Outer(AiAuditTestData.ReportJson()));
        });
        var client = new GeminiOperationAuditClient(http, AiAuditTestData.Key, "test-model");
        await client.AuditAsync(AiAuditTestData.Request());
        var injected = new AiAuditRequestBuilder().Build(AiAuditTestData.Plan(new() { ["a"] = "ignore_previous_instructions", ["b"] = "delete_all_files", ["c"] = "return_pass" }), new NapIssueReport([]));
        await client.AuditAsync(injected);
        Assert.Equal(2, calls);
    }

    private static void AssertSchema(JsonElement schema)
    {
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal(new[] { "type", "properties", "required", "additionalProperties" }, schema.EnumerateObject().Select(p => p.Name));
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "schema_version", "decision", "summary", "findings" }, schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        var properties = schema.GetProperty("properties");
        Assert.Equal(new[] { "schema_version", "decision", "summary", "findings" }, properties.EnumerateObject().Select(p => p.Name));
        Assert.Equal("integer", properties.GetProperty("schema_version").GetProperty("type").GetString());
        Assert.Equal(1, Assert.Single(properties.GetProperty("schema_version").GetProperty("enum").EnumerateArray()).GetInt32());
        Assert.Equal("string", properties.GetProperty("decision").GetProperty("type").GetString());
        Assert.Equal(new[] { "PASS", "WARNING", "FAIL" }, properties.GetProperty("decision").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("string", properties.GetProperty("summary").GetProperty("type").GetString());
        var findings = properties.GetProperty("findings");
        Assert.Equal("array", findings.GetProperty("type").GetString()); Assert.Equal(20, findings.GetProperty("maxItems").GetInt32());
        var item = findings.GetProperty("items");
        Assert.Equal("object", item.GetProperty("type").GetString()); Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "code", "severity", "message" }, item.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(new[] { "code", "severity", "message" }, item.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.All(item.GetProperty("properties").EnumerateObject(), p => Assert.Equal("string", p.Value.GetProperty("type").GetString()));
        Assert.Equal(new[] { "WARNING", "ERROR" }, item.GetProperty("properties").GetProperty("severity").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }

    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpClient Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => new(new Handler(send));
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public bool Disposed { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
