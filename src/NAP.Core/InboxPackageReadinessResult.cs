namespace NAP.Core;

/// <summary>
/// Represents the result of a bounded readiness check for an Inbox package candidate.
/// </summary>
public sealed record InboxPackageReadinessResult(InboxPackageReadinessStatus Status)
{
    /// <summary>
    /// Gets whether the candidate met every readiness check.
    /// </summary>
    public bool IsReady => Status == InboxPackageReadinessStatus.Ready;
}
