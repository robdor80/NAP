namespace NAP.Core;

/// <summary>
/// Detects ZIP package candidates by taking a snapshot of an existing Inbox.
/// </summary>
public sealed class InboxPackageDetector
{
    /// <summary>
    /// Returns the ZIP files found directly in the specified Inbox directory.
    /// </summary>
    /// <param name="inboxPath">The path of an existing Inbox directory.</param>
    /// <returns>A deterministic, name-ordered collection of ZIP candidates.</returns>
    /// <exception cref="ArgumentException">Thrown when the path is null, empty or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the Inbox directory does not exist.</exception>
    public IReadOnlyList<InboxPackageCandidate> Detect(string inboxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inboxPath);

        var normalizedPath = Path.GetFullPath(inboxPath);
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"Inbox directory does not exist: '{normalizedPath}'.");
        }

        return Enumerate(normalizedPath)
            .OrderBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.FileName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.FullPath, StringComparer.Ordinal)
            .ToArray();
    }

    // Streaming reconciliation avoids materializing an entire Inbox in the monitor's pending queue.
    internal IEnumerable<InboxPackageCandidate> Enumerate(string normalizedPath) =>
        Directory.EnumerateFiles(normalizedPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            .Select(path => new InboxPackageCandidate(Path.GetFileName(path), path));
}
