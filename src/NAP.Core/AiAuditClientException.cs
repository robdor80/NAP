namespace NAP.Core;

/// <summary>Audit did not produce a validated decision. The future orchestrator must STOP, never infer PASS.</summary>
public sealed class AiAuditClientException : Exception
{
    public AiAuditClientException(string message) : base(message) { }
    public AiAuditClientException(string message, Exception innerException) : base(message, innerException) { }
}
