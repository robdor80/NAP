namespace NAP.Core;

/// <summary>
/// Represents a ZIP file candidate detected in the Inbox.
/// </summary>
public sealed record InboxPackageCandidate(string FileName, string FullPath);
