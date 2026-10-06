namespace NAP.Core;

/// <summary>A locally validated audit decision; even PASS does not authorize execution.</summary>
public sealed class AiAuditReport
{
    public AiAuditReport(AiAuditDecision decision, string summary, IEnumerable<AiAuditFinding> findings)
    {
        if (!Enum.IsDefined(decision))
            throw new ArgumentOutOfRangeException(nameof(decision));
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (summary.Length > 2000)
            throw new ArgumentException("Audit summary cannot exceed 2000 characters.", nameof(summary));
        ArgumentNullException.ThrowIfNull(findings);
        // Bound enumeration as well as the resulting collection.
        var snapshot = findings.Take(21).ToArray();
        if (snapshot.Length > 20 || snapshot.Any(finding => finding is null))
            throw new ArgumentException("An audit allows at most 20 non-null findings.", nameof(findings));
        var coherent = decision switch
        {
            AiAuditDecision.Pass => snapshot.Length == 0,
            AiAuditDecision.Warning => snapshot.Length > 0 && snapshot.All(f => f.Severity == AiAuditFindingSeverity.Warning),
            AiAuditDecision.Fail => snapshot.Any(f => f.Severity == AiAuditFindingSeverity.Error),
            _ => false
        };
        if (!coherent)
            throw new ArgumentException("Audit findings must agree with the decision.", nameof(findings));
        Decision = decision;
        Summary = summary;
        Findings = Array.AsReadOnly(snapshot);
    }

    public AiAuditDecision Decision { get; }
    public string Summary { get; }
    public IReadOnlyList<AiAuditFinding> Findings { get; }
    public bool Passed => Decision == AiAuditDecision.Pass;
    public bool RequiresReview => Decision == AiAuditDecision.Warning;
    public bool ShouldStop => Decision == AiAuditDecision.Fail;
}
