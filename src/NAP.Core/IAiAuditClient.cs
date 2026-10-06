namespace NAP.Core;

public interface IAiAuditClient
{
    Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default);
}
