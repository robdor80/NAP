namespace NAP.Core;

/// <summary>NAP's deterministic STOP always precedes and overrides AI audit.</summary>
public sealed class AiAuditRequestBuilder
{
    public AiAuditRequest Build(ProcessingPlan plan, NapIssueReport validationReport)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(validationReport);
        if (validationReport.ShouldStop)
            throw new InvalidOperationException("A ProcessingPlan with blocking NAP validation issues cannot be sent to AI audit.");
        return new AiAuditRequest(plan.AssetKey, plan.AssetType, plan.ProductionProfile, plan.Classification,
            plan.FilesByRole.Keys, plan.ProductionDestination.RelativeDirectory,
            validationReport.Issues.Select(issue => new AiAuditIssueFact(issue.Code, issue.Severity, issue.Disposition)));
    }
}
