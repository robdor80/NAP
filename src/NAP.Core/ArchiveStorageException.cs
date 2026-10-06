namespace NAP.Core;

/// <summary>A stopped archive operation. Previously published masters are never rolled back.</summary>
public sealed class ArchiveStorageException : InvalidOperationException
{
    internal ArchiveStorageException(NapIssueReport issues, Exception? innerException = null)
        : base(string.Join(" ", issues.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), innerException)
    {
        if (!issues.ShouldStop) throw new ArgumentException("An archive failure requires a STOP issue.", nameof(issues));
        Issues = issues;
    }

    public NapIssueReport Issues { get; }

    internal static ArchiveStorageException Stop(string code, string message, string? path = null, Exception? inner = null) =>
        new(new NapIssueReport([new NapIssue(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, path)]), inner);
}
