namespace NAP.Core;

/// <summary>A known domain issue. Severity and execution disposition are independent.</summary>
public sealed record NapIssue
{
    public NapIssue(string code, NapIssueSeverity severity, NapIssueDisposition disposition,
        string message, string? subjectPath = null, string? detail = null)
    {
        if (!AssetNamingRules.IsValidMachineIdentifier(code))
            throw new ArgumentException("Issue code must be a Naming v1 machine identifier.", nameof(code));
        if (!Enum.IsDefined(severity))
            throw new ArgumentOutOfRangeException(nameof(severity));
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Severity = severity;
        Disposition = disposition;
        Message = message;
        SubjectPath = subjectPath;
        Detail = detail;
    }

    public string Code { get; }
    public NapIssueSeverity Severity { get; }
    public NapIssueDisposition Disposition { get; }
    public string Message { get; }
    public string? SubjectPath { get; }
    public string? Detail { get; }
    public bool StopsProcessing => Disposition == NapIssueDisposition.Stop;
}
