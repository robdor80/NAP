namespace NAP.Core;

/// <summary>A snapshot exists exactly when the issue report is clean.</summary>
public sealed class ProductionRepositoryScanResult
{
    public ProductionRepositoryScanResult(NapIssueReport issues, ProductionRepositorySnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.IsClean != (snapshot is not null))
            throw new ArgumentException("A repository snapshot requires a clean report, and a clean report requires a snapshot.", nameof(snapshot));
        Issues = issues;
        Snapshot = snapshot;
    }

    public NapIssueReport Issues { get; }
    public ProductionRepositorySnapshot? Snapshot { get; }
    public bool IsValid => Snapshot is not null;
}
