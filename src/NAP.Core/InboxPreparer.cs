namespace NAP.Core;

/// <summary>
/// Prepares a configured Inbox directory without monitoring or processing its contents.
/// </summary>
public sealed class InboxPreparer
{
    /// <summary>
    /// Validates, normalizes and prepares the specified Inbox directory.
    /// </summary>
    /// <param name="inboxPath">The configured path for the Inbox directory.</param>
    /// <returns>The normalized path used for the Inbox directory.</returns>
    /// <exception cref="ArgumentException">Thrown when the path is null, empty or whitespace.</exception>
    public string Prepare(string inboxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inboxPath);

        var normalizedPath = Path.GetFullPath(inboxPath);
        Directory.CreateDirectory(normalizedPath);

        return normalizedPath;
    }
}
