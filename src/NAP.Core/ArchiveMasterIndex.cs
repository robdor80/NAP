namespace NAP.Core;

/// <summary>An immutable, deterministic index. The filesystem still requires candidate-specific verification.</summary>
public sealed class ArchiveMasterIndex
{
    public ArchiveMasterIndex(UniverseId universeId, IEnumerable<ArchiveMasterIndexEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(universeId);
        ArgumentNullException.ThrowIfNull(entries);
        var snapshot = entries.ToArray();
        if (snapshot.Any(entry => entry is null)) throw new ArgumentException("Index entries cannot contain null.", nameof(entries));
        if (snapshot.Select(entry => entry.AssetId).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("Asset IDs must be unique within the index.", nameof(entries));
        UniverseId = universeId;
        Entries = Array.AsReadOnly(snapshot.OrderBy(entry => entry.AssetId, StringComparer.Ordinal).ToArray());
    }

    public UniverseId UniverseId { get; }
    public IReadOnlyList<ArchiveMasterIndexEntry> Entries { get; }
}
