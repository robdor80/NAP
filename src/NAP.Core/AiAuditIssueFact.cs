namespace NAP.Core;

/// <summary>Only the safe, stable facts of a deterministic NAP issue.</summary>
public sealed record AiAuditIssueFact
{
    public AiAuditIssueFact(string code, NapIssueSeverity severity, NapIssueDisposition disposition)
    {
        if (!AssetNamingRules.IsValidMachineIdentifier(code))
            throw new ArgumentException("Issue code must be a Naming v1 machine identifier.", nameof(code));
        if (!Enum.IsDefined(severity))
            throw new ArgumentOutOfRangeException(nameof(severity));
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        Code = code;
        Severity = severity;
        Disposition = disposition;
    }

    public string Code { get; }
    public NapIssueSeverity Severity { get; }
    public NapIssueDisposition Disposition { get; }
}
