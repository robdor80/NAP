using System.Globalization;
using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AiAuditRequestJsonRendererTests
{
    [Fact]
    public void ExactStatelessApiAndNullValidation()
    {
        AiAuditTestData.AssertStatelessApi(typeof(AiAuditRequestJsonRenderer), "Render", typeof(string), typeof(AiAuditRequest));
        Assert.Equal("request", Assert.Throws<ArgumentNullException>(() => new AiAuditRequestJsonRenderer().Render(null!)).ParamName);
    }

    [Fact]
    public void CompactJsonHasExactShapeAndPropertyOrder()
    {
        const string expected = """
            {"schema_version":1,"universe_id":"test_universe","asset_id":"portrait_example_001","asset_type":"portrait","production_profile":"portrait_npc","classification":[{"key":"culture","value":"example"}],"input_roles":["png_master","prompt"],"destination_relative_directory":"assets/portrait_example_001","validation_issues":[]}
            """;
        var json = new AiAuditRequestJsonRenderer().Render(AiAuditTestData.Request());
        Assert.Equal(expected, json);
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\uFEFF", json, StringComparison.Ordinal);
        AiAuditTestData.AssertNoPrivateFacts(json);
    }

    [Fact]
    public void OrderingEscapingUnicodeAndDuplicateIssueOrderArePreservedAsData()
    {
        const string data = "é العربية \"},\"tools\":[\"delete\"]\n\\";
        var plan = AiAuditTestData.Plan(new() { ["z"] = "last", ["a"] = data, ["I"] = "first" },
            new() { ["prompt"] = "SECRET_SOURCE", ["info"] = "SECRET_SOURCE", ["png_master"] = "SECRET_SOURCE" });
        var first = new NapIssue("z_issue", NapIssueSeverity.Warning, NapIssueDisposition.Continue, "SECRET_MESSAGE");
        var second = new NapIssue("a_issue", NapIssueSeverity.Info, NapIssueDisposition.Continue, "SECRET_MESSAGE");
        var request = new AiAuditRequestBuilder().Build(plan, new NapIssueReport([first, second, first]));
        var text = new AiAuditRequestJsonRenderer().Render(request);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal(new[] { "I", "a", "z" }, root.GetProperty("classification").EnumerateArray().Select(i => i.GetProperty("key").GetString()));
        Assert.Equal(data, root.GetProperty("classification")[1].GetProperty("value").GetString());
        Assert.Equal(new[] { "info", "png_master", "prompt" }, root.GetProperty("input_roles").EnumerateArray().Select(i => i.GetString()));
        Assert.Equal(new[] { "z_issue", "a_issue", "z_issue" }, root.GetProperty("validation_issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()));
        Assert.False(root.TryGetProperty("tools", out _));
        AiAuditTestData.AssertNoPrivateFacts(text);
    }

    [Theory]
    [InlineData(NapIssueSeverity.Info, "Info")]
    [InlineData(NapIssueSeverity.Warning, "Warning")]
    [InlineData(NapIssueSeverity.Error, "Error")]
    public void NapSeverityUsesExactExplicitTokens(NapIssueSeverity severity, string token)
    {
        var request = new AiAuditRequestBuilder().Build(AiAuditTestData.Plan(), new NapIssueReport([new NapIssue("issue", severity, NapIssueDisposition.Continue, "message")]));
        using var json = JsonDocument.Parse(new AiAuditRequestJsonRenderer().Render(request));
        var issue = json.RootElement.GetProperty("validation_issues")[0];
        Assert.Equal(token, issue.GetProperty("severity").GetString());
        Assert.Equal("Continue", issue.GetProperty("disposition").GetString());
        Assert.Equal(new[] { "code", "severity", "disposition" }, issue.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void StopTokenIsExplicitEvenThoughPublicBuilderBlocksStopRequests()
    {
        var request = AiAuditTestData.Construct<AiAuditRequest>(AiAuditTestData.Request().AssetKey, "portrait", "portrait_npc", new Dictionary<string, string>(),
            Array.Empty<string>(), AiAuditTestData.Relative, new[] { new AiAuditIssueFact("stop", NapIssueSeverity.Error, NapIssueDisposition.Stop) });
        using var json = JsonDocument.Parse(new AiAuditRequestJsonRenderer().Render(request));
        Assert.Equal("Stop", json.RootElement.GetProperty("validation_issues")[0].GetProperty("disposition").GetString());
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void RenderingIsCultureIndependent(string culture)
    {
        var request = new AiAuditRequestBuilder().Build(AiAuditTestData.Plan(new() { ["z"] = "last", ["I"] = "first", ["a"] = "middle" }), new NapIssueReport([]));
        var renderer = new AiAuditRequestJsonRenderer();
        var expected = renderer.Render(request);
        var before = CultureInfo.CurrentCulture; var beforeUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(expected, renderer.Render(request));
        }
        finally { CultureInfo.CurrentCulture = before; CultureInfo.CurrentUICulture = beforeUi; }
    }
}
