namespace NAP.Core;

public enum AiAuditErrorKind { Unknown, Transport, RateLimited, Server, Timeout, Unauthorized, Forbidden, InvalidResponse, Configuration, Client }

/// <summary>Audit did not produce a validated decision. The future orchestrator must STOP, never infer PASS.</summary>
public sealed class AiAuditClientException : Exception
{
    public AiAuditErrorKind Code { get; }
    public int? HttpStatus { get; }
    public TimeSpan? RetryAfter { get; }
    public AiAuditClientException(AiAuditErrorKind code, string message, int? httpStatus = null, TimeSpan? retryAfter = null) : base(message)
    { Code = code; HttpStatus = httpStatus; RetryAfter = retryAfter; }
    public AiAuditClientException(AiAuditErrorKind code, string message, Exception innerException) : base(message, innerException)
    { Code = code; }
    public AiAuditClientException(string message) : base(message) { }
    public AiAuditClientException(string message, Exception innerException) : base(message, innerException) { }
}
