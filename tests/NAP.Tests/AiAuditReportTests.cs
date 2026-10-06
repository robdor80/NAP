using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AiAuditReportTests
{
    [Fact]
    public void PublicSealedClassHasExactConstructorAndSixGetOnlyProperties()
    {
        var type = typeof(AiAuditReport);
        Assert.True(type.IsPublic); Assert.True(type.IsSealed);
        var parameters = Assert.Single(type.GetConstructors()).GetParameters();
        Assert.Equal(new[] { "decision", "summary", "findings" }, parameters.Select(p => p.Name));
        Assert.Equal(new[] { typeof(AiAuditDecision), typeof(string), typeof(IEnumerable<AiAuditFinding>) }, parameters.Select(p => p.ParameterType));
        var properties = type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Decision", "Findings", "Passed", "RequiresReview", "ShouldStop", "Summary" }, properties.Select(p => p.Name));
        Assert.All(properties, p => Assert.Null(p.SetMethod));
    }

    public static IEnumerable<object[]> Matrix()
    {
        foreach (var decision in Enum.GetValues<AiAuditDecision>())
            foreach (var shape in new[] { "empty", "warning", "error", "mixed" })
                yield return new object[] { decision, shape };
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void DecisionFindingMatrixAndDerivedFlagsAreExact(AiAuditDecision decision, string shape)
    {
        AiAuditFinding[] findings = shape switch
        {
            "empty" => [], "warning" => [AiAuditTestData.Finding()], "error" => [AiAuditTestData.Finding(AiAuditFindingSeverity.Error)],
            _ => [AiAuditTestData.Finding(), AiAuditTestData.Finding(AiAuditFindingSeverity.Error)]
        };
        var valid = decision == AiAuditDecision.Pass ? shape == "empty" : decision == AiAuditDecision.Warning ? shape == "warning" : shape is "error" or "mixed";
        if (!valid) { Assert.Throws<ArgumentException>(() => new AiAuditReport(decision, "summary", findings)); return; }
        var report = new AiAuditReport(decision, "summary", findings);
        Assert.Equal(decision == AiAuditDecision.Pass, report.Passed);
        Assert.Equal(decision == AiAuditDecision.Warning, report.RequiresReview);
        Assert.Equal(decision == AiAuditDecision.Fail, report.ShouldStop);
        Assert.Equal(findings, report.Findings);
    }

    [Theory]
    [InlineData(-1)] [InlineData(3)] [InlineData(int.MaxValue)]
    public void InvalidDecisionIsRejected(int value) => Assert.Throws<ArgumentOutOfRangeException>(() => new AiAuditReport((AiAuditDecision)value, "summary", []));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" \n\t")]
    public void SummaryRequired(string? summary) => Assert.ThrowsAny<ArgumentException>(() => new AiAuditReport(AiAuditDecision.Pass, summary!, []));

    [Fact]
    public void SummaryLengthAndFindingCountBoundariesAreExact()
    {
        Assert.Equal(2000, new AiAuditReport(AiAuditDecision.Pass, new string('é', 2000), []).Summary.Length);
        Assert.Throws<ArgumentException>(() => new AiAuditReport(AiAuditDecision.Pass, new string('a', 2001), []));
        Assert.Equal(20, new AiAuditReport(AiAuditDecision.Warning, "summary", Enumerable.Repeat(AiAuditTestData.Finding(), 20)).Findings.Count);
        Assert.Throws<ArgumentException>(() => new AiAuditReport(AiAuditDecision.Warning, "summary", Enumerable.Repeat(AiAuditTestData.Finding(), 21)));
        Assert.Throws<ArgumentNullException>(() => new AiAuditReport(AiAuditDecision.Pass, "summary", null!));
        Assert.Throws<ArgumentException>(() => new AiAuditReport(AiAuditDecision.Warning, "summary", [null!]));
    }

    [Fact]
    public void ReportCopiesCallerCollectionAndExposesReadOnlyFindings()
    {
        var finding = AiAuditTestData.Finding();
        var list = new List<AiAuditFinding> { finding };
        var report = new AiAuditReport(AiAuditDecision.Warning, " summary ", list);
        list.Clear();
        Assert.Same(finding, Assert.Single(report.Findings));
        Assert.Equal(" summary ", report.Summary);
        var exposed = Assert.IsAssignableFrom<IList<AiAuditFinding>>(report.Findings);
        Assert.True(exposed.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => exposed.Clear());
        Assert.Throws<NotSupportedException>(() => exposed.Add(finding));
        Assert.Throws<NotSupportedException>(() => exposed[0] = finding);
    }
}
