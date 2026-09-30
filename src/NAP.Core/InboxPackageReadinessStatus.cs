namespace NAP.Core;

/// <summary>
/// Describes the availability observed for an Inbox package candidate.
/// </summary>
public enum InboxPackageReadinessStatus
{
    Ready,
    Missing,
    Changing,
    InUse
}
