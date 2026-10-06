namespace NAP.Core;

/// <summary>A stopped production operation; correctly published files and durable checkpoints remain recoverable.</summary>
public sealed class ProductionStorageException : InvalidOperationException
{
    internal ProductionStorageException(NapIssueReport issues, Exception? inner = null)
        : base(string.Join(" ", issues.Issues.Select(i => $"{i.Code}: {i.Message}")), inner)
    {
        if (!issues.ShouldStop) throw new ArgumentException("A production failure requires STOP.", nameof(issues));
        Issues = issues;
    }

    public NapIssueReport Issues { get; }

    internal static ProductionStorageException Stop(string code, string message, string? path = null, Exception? inner = null) =>
        new(new NapIssueReport([new NapIssue(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, path)]), inner);
}
