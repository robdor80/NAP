namespace NAP.Core;

/// <summary>A defensive, ordinally sorted observation of a validated repository. Construction does no I/O.</summary>
public sealed class ProductionRepositorySnapshot
{
    internal ProductionRepositorySnapshot(ValidatedProductionRepository repository, IEnumerable<ProductionRepositoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(entries);
        var snapshot = entries.ToArray();
        if (snapshot.Any(entry => entry is null))
            throw new ArgumentException("A snapshot cannot contain null entries.", nameof(entries));
        UniverseId = repository.UniverseId;
        RootPath = repository.RootPath;
        Entries = Array.AsReadOnly(snapshot.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToArray());
    }

    public UniverseId UniverseId { get; }
    public string RootPath { get; }
    public IReadOnlyList<ProductionRepositoryEntry> Entries { get; }
}
