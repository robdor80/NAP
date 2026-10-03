namespace NAP.Core;

/// <summary>An immutable enumerated descendant. Names are preserved without applying asset naming rules.</summary>
public sealed class ProductionRepositoryEntry
{
    internal ProductionRepositoryEntry(string relativePath, string fullPath, ProductionRepositoryEntryKind kind)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(fullPath);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        RelativePath = relativePath;
        FullPath = fullPath;
        Kind = kind;
    }

    public string RelativePath { get; }
    public string FullPath { get; }
    public ProductionRepositoryEntryKind Kind { get; }
}
