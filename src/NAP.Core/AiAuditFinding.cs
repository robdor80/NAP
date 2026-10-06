namespace NAP.Core;

/// <summary>An untrusted auditor observation, without paths or execution capabilities.</summary>
public sealed record AiAuditFinding
{
    public AiAuditFinding(string code, AiAuditFindingSeverity severity, string message)
    {
        if (!AssetNamingRules.IsValidMachineIdentifier(code))
            throw new ArgumentException("Finding code must be a Naming v1 machine identifier.", nameof(code));
        if (!Enum.IsDefined(severity))
            throw new ArgumentOutOfRangeException(nameof(severity));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Length > 1000)
            throw new ArgumentException("Finding message cannot exceed 1000 characters.", nameof(message));
        Code = code;
        Severity = severity;
        Message = message;
    }

    public string Code { get; }
    public AiAuditFindingSeverity Severity { get; }
    public string Message { get; }
}
