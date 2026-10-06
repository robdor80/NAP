using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AiAuditRequestBuilderTests
{
    [Fact]
    public void ExactStatelessApiAndNullValidation()
    {
        AiAuditTestData.AssertStatelessApi(typeof(AiAuditRequestBuilder), "Build", typeof(AiAuditRequest), typeof(ProcessingPlan), typeof(NapIssueReport));
        var builder = new AiAuditRequestBuilder();
        Assert.Equal("plan", Assert.Throws<ArgumentNullException>(() => builder.Build(null!, new NapIssueReport([]))).ParamName);
        Assert.Equal("validationReport", Assert.Throws<ArgumentNullException>(() => builder.Build(AiAuditTestData.Plan(), null!)).ParamName);
    }

    [Fact]
    public void CleanPlanProducesOnlyExactAllowlistedMetadataAndRoleKeys()
    {
        var plan = AiAuditTestData.Plan();
        var request = new AiAuditRequestBuilder().Build(plan, new NapIssueReport([]));
        Assert.Same(plan.AssetKey, request.AssetKey);
        Assert.Equal(plan.AssetType, request.AssetType);
        Assert.Equal(plan.ProductionProfile, request.ProductionProfile);
        Assert.Equal(plan.Classification, request.Classification);
        Assert.NotSame(plan.Classification, request.Classification);
        Assert.Equal(plan.FilesByRole.Keys.OrderBy(role => role, StringComparer.Ordinal), request.InputRoles);
        Assert.Equal(AiAuditTestData.Relative, request.DestinationRelativeDirectory);
        Assert.Empty(request.ValidationIssues);
        var json = new AiAuditRequestJsonRenderer().Render(request);
        AiAuditTestData.AssertNoPrivateFacts(json);
        foreach (var path in plan.FilesByRole.Values) Assert.DoesNotContain(path, json);
    }

    [Theory]
    [InlineData(NapIssueSeverity.Info)]
    [InlineData(NapIssueSeverity.Warning)]
    [InlineData(NapIssueSeverity.Error)]
    public void StopOverridesAnySeverityAndThrowsBeforeAiCanBeCalled(NapIssueSeverity severity)
    {
        var report = new NapIssueReport([new NapIssue("block", severity, NapIssueDisposition.Stop, "SECRET_MESSAGE")]);
        var clientCalls = 0;
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            var request = new AiAuditRequestBuilder().Build(AiAuditTestData.Plan(), report);
            clientCalls++; // This would dispatch to a provider only after a successful Build.
            _ = request;
        });
        Assert.Equal("A ProcessingPlan with blocking NAP validation issues cannot be sent to AI audit.", exception.Message);
        Assert.Equal(0, clientCalls);
    }

    [Fact]
    public void ContinueIssuesRetainOrderAndDuplicatesButOnlyThreeSafeFacts()
    {
        var first = new NapIssue("z_issue", NapIssueSeverity.Warning, NapIssueDisposition.Continue,
            "SECRET_MESSAGE", @"C:\SECRET_SUBJECT", "SECRET_DETAIL");
        var second = new NapIssue("a_issue", NapIssueSeverity.Info, NapIssueDisposition.Continue, "SECRET_MESSAGE");
        var request = new AiAuditRequestBuilder().Build(AiAuditTestData.Plan(), new NapIssueReport([first, second, first]));
        Assert.Equal(new[] { "z_issue", "a_issue", "z_issue" }, request.ValidationIssues.Select(i => i.Code));
        Assert.Equal(first.Severity, request.ValidationIssues[0].Severity);
        Assert.Equal(first.Disposition, request.ValidationIssues[0].Disposition);
        Assert.Equal(request.ValidationIssues[0], request.ValidationIssues[2]);
        AiAuditTestData.AssertNoPrivateFacts(new AiAuditRequestJsonRenderer().Render(request));
    }
}
