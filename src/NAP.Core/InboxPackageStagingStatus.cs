namespace NAP.Core;

/// <summary>
/// Describes the outcome of a staging attempt.
/// </summary>
public enum InboxPackageStagingStatus
{
    Staged,
    NotReady,
    Collision
}
