using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AiAuditReportJsonParserTests
{
    [Fact]
    public void ExactStatelessApiAndNullValidation()
    {
        AiAuditTestData.AssertStatelessApi(typeof(AiAuditReportJsonParser), "Parse", typeof(AiAuditReport), typeof(string));
        Assert.Equal("json", Assert.Throws<ArgumentNullException>(() => new AiAuditReportJsonParser().Parse(null!)).ParamName);
    }

    [Theory]
    [InlineData("PASS", AiAuditDecision.Pass)]
    [InlineData("WARNING", AiAuditDecision.Warning)]
    [InlineData("FAIL", AiAuditDecision.Fail)]
    public void ExactDecisionAndSeverityTokensProduceValidatedReports(string token, AiAuditDecision decision)
    {
        var findings = token == "PASS" ? "[]" : "[" + AiAuditTestData.FindingJson(token == "FAIL" ? "ERROR" : "WARNING") + "]";
        var report = new AiAuditReportJsonParser().Parse(AiAuditTestData.ReportJson(token, "Coherent é العربية.", findings));
        Assert.Equal(decision, report.Decision);
        Assert.Equal("Coherent é العربية.", report.Summary);
        if (token != "PASS") Assert.Equal(token == "FAIL" ? AiAuditFindingSeverity.Error : AiAuditFindingSeverity.Warning, Assert.Single(report.Findings).Severity);
    }

    public static IEnumerable<object[]> InvalidDocuments()
    {
        var pass = AiAuditTestData.ReportJson();
        foreach (var text in new[] { "", " ", "{", "null", "[]", "true", "123", "\"text\"", "```json\n" + pass + "\n```", "prefix " + pass,
            pass + " suffix", pass + pass, pass.Replace("\"schema_version\":1", "\"schema_version\":2"),
            pass.Replace("\"schema_version\":1", "\"schema_version\":1.0"), pass.Replace("\"schema_version\":1", "\"schema_version\":1e0"),
            pass.Replace("\"schema_version\":1", "\"schema_version\":\"1\""),
            pass.Replace("\"decision\":\"PASS\"", "\"decision\":0"),
            AiAuditTestData.ReportJson("pass"), AiAuditTestData.ReportJson("warning"), AiAuditTestData.ReportJson("FAIL "), AiAuditTestData.ReportJson("UNKNOWN"),
            AiAuditTestData.ReportJson(summary: ""), AiAuditTestData.ReportJson(summary: " \n\t"), AiAuditTestData.ReportJson(summary: new string('a', 2001)),
            AiAuditTestData.ReportJson("PASS", findings: "[" + AiAuditTestData.FindingJson() + "]"),
            AiAuditTestData.ReportJson("WARNING"), AiAuditTestData.ReportJson("FAIL"),
            AiAuditTestData.ReportJson("WARNING", findings: "[" + AiAuditTestData.FindingJson("ERROR") + "]"),
            AiAuditTestData.ReportJson("FAIL", findings: "[" + AiAuditTestData.FindingJson() + "]"),
            AiAuditTestData.ReportJson("WARNING", findings: "[null]"), AiAuditTestData.ReportJson("WARNING", findings: "[[]]"),
            AiAuditTestData.ReportJson("WARNING", findings: "[" + string.Join(',', Enumerable.Repeat(AiAuditTestData.FindingJson(), 21)) + "]") })
            yield return new object[] { text };
        foreach (var name in new[] { "schema_version", "decision", "summary", "findings" })
        {
            var root = JsonNode.Parse(pass)!.AsObject(); root.Remove(name); yield return new object[] { root.ToJsonString() };
            var value = JsonNode.Parse(pass)![name]!.ToJsonString();
            yield return new object[] { pass.Replace("\"" + name + "\":" + value, "\"" + name + "\":" + value + ",\"" + name + "\":" + value) };
            foreach (var wrong in new[] { "null", "true", "{}" })
                yield return new object[] { pass.Replace("\"" + name + "\":" + value, "\"" + name + "\":" + wrong) };
        }
        yield return new object[] { pass.Replace("\"findings\":[]", "\"extra\":true,\"findings\":[]") };
        yield return new object[] { pass.Replace("\"findings\":[]", "\"findings\":\"[]\"") };
        yield return new object[] { pass.Replace("\"summary\":\"Coherent.\"", "\"summary\":123") };
        foreach (var severity in new[] { "warning", "error", "INFO", "UNKNOWN", " WARNING" })
            yield return new object[] { AiAuditTestData.ReportJson("WARNING", findings: "[" + AiAuditTestData.FindingJson(severity) + "]") };
        foreach (var code in new[] { "", " ", "Bad", "../path", "a__b", new string('a', 65) })
            yield return new object[] { AiAuditTestData.ReportJson("WARNING", findings: "[" + AiAuditTestData.FindingJson(code: code) + "]") };
        foreach (var message in new[] { "", " \n\t", new string('a', 1001) })
            yield return new object[] { AiAuditTestData.ReportJson("WARNING", findings: "[" + AiAuditTestData.FindingJson(message: message) + "]") };
        var finding = AiAuditTestData.FindingJson();
        foreach (var name in new[] { "code", "severity", "message" })
        {
            var root = JsonNode.Parse(finding)!.AsObject(); root.Remove(name);
            yield return new object[] { AiAuditTestData.ReportJson("WARNING", findings: "[" + root.ToJsonString() + "]") };
            var value = JsonNode.Parse(finding)![name]!.ToJsonString();
            foreach (var wrong in new[] { "null", "1", "true", "[]", "{}" })
                yield return new object[] { AiAuditTestData.ReportJson("WARNING", findings: "[" + finding.Replace("\"" + name + "\":" + value, "\"" + name + "\":" + wrong) + "]") };
            yield return new object[] { AiAuditTestData.ReportJson("WARNING", findings: "[" + finding.Replace("\"" + name + "\":" + value, "\"" + name + "\":" + value + ",\"" + name + "\":" + value) + "]") };
        }
        yield return new object[] { AiAuditTestData.ReportJson("WARNING", findings: "[" + finding.Replace("\"code\":", "\"extra\":true,\"code\":") + "]") };
        yield return new object[] { pass.Replace("\"schema_version\"", "\"Schema_version\"") };
        yield return new object[] { pass.Replace("Coherent.", "\\uD800") };
        yield return new object[] { pass.Replace("[]}", "[],}") };
        yield return new object[] { "/*comment*/" + pass };
    }

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public void RejectsEveryMalformedAmbiguousOrSemanticallyInvalidReportWithoutEchoingInput(string json)
    {
        var exception = Assert.Throws<InvalidDataException>(() => new AiAuditReportJsonParser().Parse(json));
        Assert.Equal("The AI audit report must satisfy the exact JSON v1 contract and decision invariants.", exception.Message);
    }

    [Fact]
    public void ValidBoundariesMixedFailAndWhitespacePropertyOrderAreAcceptedWithoutNormalization()
    {
        var findings = "[" + AiAuditTestData.FindingJson("ERROR", message: new string('é', 1000)) + "," +
            string.Join(',', Enumerable.Repeat(AiAuditTestData.FindingJson(), 19)) + "]";
        var report = new AiAuditReportJsonParser().Parse(AiAuditTestData.ReportJson("FAIL", new string('é', 2000), findings));
        Assert.Equal(20, report.Findings.Count); Assert.True(report.ShouldStop);
        var reordered = " { \"findings\": [], \"summary\": \" spaced \", \"decision\": \"PASS\", \"schema_version\": 1 } ";
        Assert.Equal(" spaced ", new AiAuditReportJsonParser().Parse(reordered).Summary);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void ParserTokensAndSummaryAreCultureIndependent(string culture)
    {
        var previous = CultureInfo.CurrentCulture; var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Assert.True(new AiAuditReportJsonParser().Parse(AiAuditTestData.ReportJson()).Passed);
            Assert.Throws<InvalidDataException>(() => new AiAuditReportJsonParser().Parse(AiAuditTestData.ReportJson("pass")));
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }
}
