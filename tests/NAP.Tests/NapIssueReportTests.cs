using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class NapIssueReportTests
{
    [Fact]
    public void EmptyReport_IsCleanAndCanContinue()
    {
        var report = new NapIssueReport([]);
        Assert.Empty(report.Issues);
        Assert.True(report.IsClean);
        Assert.False(report.HasErrors);
        Assert.False(report.ShouldStop);
        Assert.True(report.CanContinue);
    }

    [Theory]
    [InlineData(NapIssueSeverity.Info, NapIssueDisposition.Continue, false, false)]
    [InlineData(NapIssueSeverity.Warning, NapIssueDisposition.Stop, false, true)]
    [InlineData(NapIssueSeverity.Error, NapIssueDisposition.Continue, true, false)]
    public void SeverityAndDisposition_AreIndependent(NapIssueSeverity severity,
        NapIssueDisposition disposition, bool hasErrors, bool shouldStop)
    {
        var issue = new NapIssue(NapIssueCodes.PngInvalid, severity, disposition, "Fallback message.");
        var report = new NapIssueReport([issue]);
        Assert.False(report.IsClean);
        Assert.Equal(hasErrors, report.HasErrors);
        Assert.Equal(shouldStop, report.ShouldStop);
        Assert.Equal(!shouldStop, report.CanContinue);
        Assert.Equal(shouldStop, issue.StopsProcessing);
    }

    [Fact]
    public void Report_PreservesOrderAndCopiesSourceWithoutExposingMutableStorage()
    {
        var info = new NapIssue("test_info", NapIssueSeverity.Info, NapIssueDisposition.Continue, "Information.");
        var warning = new NapIssue(NapIssueCodes.InboxChanging, NapIssueSeverity.Warning, NapIssueDisposition.Stop, "Changing.");
        var error = new NapIssue(NapIssueCodes.PngInvalid, NapIssueSeverity.Error, NapIssueDisposition.Continue, "Invalid.");
        NapIssue[] source = [info, warning, error];
        var report = new NapIssueReport(source);
        source[0] = error;

        Assert.Equal(new[] { info, warning, error }, report.Issues);
        Assert.True(report.HasErrors);
        Assert.True(report.ShouldStop);
        Assert.False(report.CanContinue);
        var exposed = Assert.IsAssignableFrom<IList<NapIssue>>(report.Issues);
        Assert.True(exposed.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => exposed[0] = error);
        Assert.Throws<NotSupportedException>(() => exposed.Add(info));
        Assert.Throws<NotSupportedException>(() => exposed.Clear());
        Assert.Equal(new[] { info, warning, error }, report.Issues);
    }

    [Fact]
    public void NullCollectionOrEntry_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new NapIssueReport(null!));
        Assert.Throws<ArgumentException>(() => new NapIssueReport([null!]));
    }
}
