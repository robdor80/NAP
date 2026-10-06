namespace NAP.Core;

public sealed class CatalogException : IOException
{
    public CatalogException(NapIssueReport issues, Exception? inner = null) : base(string.Join("; ", issues.Issues.Select(i => i.Message)), inner)
    { ArgumentNullException.ThrowIfNull(issues); Issues = issues; }
    public NapIssueReport Issues { get; }
    internal static CatalogException Stop(string code, string message, string? path = null, Exception? inner = null) =>
        new(new NapIssueReport([new NapIssue(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, path)]), inner);
}
