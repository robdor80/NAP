namespace NAP.Core;

/// <summary>Compares only materialized snapshot structure. Performs no filesystem I/O or path normalization.</summary>
public sealed class ProductionRepositoryStructureDiffer
{
    public ProductionRepositoryStructureDiff Compare(ProductionRepositorySnapshot before, ProductionRepositorySnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.UniverseId != after.UniverseId)
            throw new ArgumentException("Snapshots must belong to the same universe.", nameof(after));
        var rootComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(before.RootPath, after.RootPath, rootComparison))
            throw new ArgumentException("Snapshots must have the same production root.", nameof(after));

        var beforeEntries = Index(before, nameof(before));
        var afterEntries = Index(after, nameof(after));
        var changes = new List<ProductionRepositoryStructuralChange>();
        foreach (var path in beforeEntries.Keys.Union(afterEntries.Keys, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
        {
            var hasBefore = beforeEntries.TryGetValue(path, out var beforeKind);
            var hasAfter = afterEntries.TryGetValue(path, out var afterKind);
            if (!hasBefore)
                changes.Add(new ProductionRepositoryStructuralChange(path, RepositoryStructuralChangeKind.Added, null, afterKind));
            else if (!hasAfter)
                changes.Add(new ProductionRepositoryStructuralChange(path, RepositoryStructuralChangeKind.Removed, beforeKind, null));
            else if (beforeKind != afterKind)
                changes.Add(new ProductionRepositoryStructuralChange(path, RepositoryStructuralChangeKind.KindChanged, beforeKind, afterKind));
        }
        return new ProductionRepositoryStructureDiff(before, changes);
    }

    private static Dictionary<string, ProductionRepositoryEntryKind> Index(ProductionRepositorySnapshot snapshot, string parameterName)
    {
        var entries = new Dictionary<string, ProductionRepositoryEntryKind>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Entries)
            if (!entries.TryAdd(entry.RelativePath, entry.Kind))
                throw new ArgumentException("Snapshot entries must have unique ordinal relative paths.", parameterName);
        return entries;
    }
}
