namespace NAP.Core;

/// <summary>An immutable, defensively copied structural diff, ordered by exact relative path.</summary>
public sealed class ProductionRepositoryStructureDiff
{
    internal ProductionRepositoryStructureDiff(ProductionRepositorySnapshot repository,
        IEnumerable<ProductionRepositoryStructuralChange> changes)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(changes);
        var snapshot = changes.ToArray();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in snapshot)
        {
            if (change is null || !paths.Add(change.RelativePath))
                throw new ArgumentException("Changes must be non-null and unique by ordinal relative path.", nameof(changes));
        }
        UniverseId = repository.UniverseId;
        RootPath = repository.RootPath;
        Changes = Array.AsReadOnly(snapshot.OrderBy(change => change.RelativePath, StringComparer.Ordinal).ToArray());
    }

    public UniverseId UniverseId { get; }
    public string RootPath { get; }
    public IReadOnlyList<ProductionRepositoryStructuralChange> Changes { get; }
    public bool IsEmpty => Changes.Count == 0;
}
