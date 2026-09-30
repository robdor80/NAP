namespace NAP.Core;

/// <summary>
/// Represents the outcome of staging an Inbox package candidate.
/// </summary>
public sealed record InboxPackageStagingResult(
    InboxPackageStagingStatus Status,
    string SourcePath,
    string FinalStagedPath,
    InboxPackageReadinessStatus? ReadinessStatus = null)
{
    /// <summary>
    /// Gets whether the package was copied successfully to its final staging path.
    /// </summary>
    public bool IsStaged => Status == InboxPackageStagingStatus.Staged;
}
