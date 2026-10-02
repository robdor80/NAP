namespace NAP.Core;

/// <summary>An ordered snapshot of issues, independent of its source collection.</summary>
public sealed class NapIssueReport
{
    public NapIssueReport(IEnumerable<NapIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var snapshot = issues.ToArray();
        if (snapshot.Any(issue => issue is null))
            throw new ArgumentException("A report cannot contain null issues.", nameof(issues));
        Issues = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<NapIssue> Issues { get; }
    public bool IsClean => Issues.Count == 0;
    public bool HasErrors => Issues.Any(issue => issue.Severity == NapIssueSeverity.Error);
    public bool ShouldStop => Issues.Any(issue => issue.StopsProcessing);
    public bool CanContinue => !ShouldStop;
}
