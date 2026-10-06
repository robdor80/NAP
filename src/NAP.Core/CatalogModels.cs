using System.Collections.ObjectModel;

namespace NAP.Core;

public enum CatalogFileLocation { Archive, Production }
public enum CatalogTraitType { String, Number, Boolean }
public sealed record CatalogVisualTrait(string Key, string Value, CatalogTraitType Type);
public sealed record CatalogFile(CatalogFileLocation Location, string Role, string Kind, string RelativePath, Sha256Digest Digest, long SizeBytes, bool Verified);

/// <summary>Original bytes, never a reserialization of the source document.</summary>
public sealed class CatalogDocument
{
    private readonly byte[] _bytes;
    internal CatalogDocument(string role, byte[] bytes) { Role = role; _bytes = (byte[])bytes.Clone(); }
    public string Role { get; }
    public byte[] ToArray() => (byte[])_bytes.Clone();
}

/// <summary>Physical facts and optional actual audit metadata. No dates, roots, inferred decision or original Job dependency.</summary>
public sealed class CatalogAssetSnapshot
{
    internal CatalogAssetSnapshot(UniverseAssetKey key, string type, string profile, string relative,
        Sha256Digest masterDigest, long masterSize, Sha256Digest productionDigest, long productionSize,
        IReadOnlyDictionary<string, string> classification, IEnumerable<CatalogFile> files,
        IEnumerable<CatalogDocument> documents, IEnumerable<CatalogVisualTrait> traits, string? auditState = null)
    {
        AssetKey = key; AssetType = type; ProductionProfile = profile; ArchiveRelativeDirectory = relative; ProductionRelativeDirectory = relative;
        MasterDigest = masterDigest; MasterSizeBytes = masterSize; ProductionDigest = productionDigest; ProductionSizeBytes = productionSize;
        Classification = new ReadOnlyDictionary<string, string>(classification.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        Files = Array.AsReadOnly(files.OrderBy(f => f.Location).ThenBy(f => f.Role, StringComparer.Ordinal).ToArray());
        Documents = Array.AsReadOnly(documents.OrderBy(d => d.Role, StringComparer.Ordinal).ToArray());
        Traits = Array.AsReadOnly(traits.OrderBy(t => t.Key, StringComparer.Ordinal).ToArray());
        AuditState = auditState;
    }
    public UniverseAssetKey AssetKey { get; }
    public string AssetType { get; }
    public string ProductionProfile { get; }
    public string Lifecycle => "physically_verified";
    public string? AuditState { get; }
    public string ArchiveRelativeDirectory { get; }
    public string ProductionRelativeDirectory { get; }
    public Sha256Digest MasterDigest { get; }
    public long MasterSizeBytes { get; }
    public Sha256Digest ProductionDigest { get; }
    public long ProductionSizeBytes { get; }
    public IReadOnlyDictionary<string, string> Classification { get; }
    public IReadOnlyList<CatalogFile> Files { get; }
    public IReadOnlyList<CatalogDocument> Documents { get; }
    public IReadOnlyList<CatalogVisualTrait> Traits { get; }
}

/// <summary>Exact AND criteria; trait paths are RFC 6901 JSON Pointers. No SQL or semantic synonyms.</summary>
public sealed class CatalogFilter
{
    public CatalogFilter(string? assetId = null, string? assetType = null, string? productionProfile = null,
        IReadOnlyDictionary<string, string>? classification = null, IEnumerable<CatalogVisualTrait>? traits = null)
    {
        AssetId = assetId; AssetType = assetType; ProductionProfile = productionProfile;
        Classification = new ReadOnlyDictionary<string, string>((classification ?? new Dictionary<string, string>())
            .ToDictionary(p => Required(p.Key), p => Required(p.Value), StringComparer.Ordinal));
        var snapshot = (traits ?? []).ToArray();
        if (snapshot.Any(t => t is null || t.Key is null || t.Value is null || !Enum.IsDefined(t.Type))) throw new ArgumentException("Invalid trait criteria.", nameof(traits));
        Traits = Array.AsReadOnly(snapshot.OrderBy(t => t.Key, StringComparer.Ordinal).ThenBy(t => t.Type).ThenBy(t => t.Value, StringComparer.Ordinal).ToArray());
    }
    public string? AssetId { get; }
    public string? AssetType { get; }
    public string? ProductionProfile { get; }
    public IReadOnlyDictionary<string, string> Classification { get; }
    public IReadOnlyList<CatalogVisualTrait> Traits { get; }
    internal static string Required(string value) { ArgumentNullException.ThrowIfNull(value); return value; }
}

public sealed record CatalogDistribution(string Dimension, string Value, long Count, CatalogTraitType? TraitType = null);
public sealed record CatalogStatistics(long TotalAssets, IReadOnlyList<CatalogDistribution> AssetTypes,
    IReadOnlyList<CatalogDistribution> ProductionProfiles, IReadOnlyList<CatalogDistribution> Classifications, IReadOnlyList<CatalogDistribution> Traits);
public sealed record CatalogCoverage
{
    public CatalogCoverage(long actualCount, long targetCount)
    {
        if (actualCount < 0) throw new ArgumentOutOfRangeException(nameof(actualCount));
        if (targetCount <= 0) throw new ArgumentOutOfRangeException(nameof(targetCount));
        ActualCount = actualCount; TargetCount = targetCount;
    }
    public long ActualCount { get; }
    public long TargetCount { get; }
    public long Remaining => Math.Max(0, TargetCount - ActualCount);
    public double CompletionRatio => (double)ActualCount / TargetCount;
    public bool IsComplete => ActualCount >= TargetCount;
}

/// <summary>User-entered operational data; not derivable from assets.</summary>
public sealed class CatalogObjective
{
    public CatalogObjective(UniverseId universeId, string id, string name, CatalogFilter filter, long targetCount)
    {
        ArgumentNullException.ThrowIfNull(universeId); ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(filter); if (targetCount <= 0) throw new ArgumentOutOfRangeException(nameof(targetCount));
        UniverseId = universeId; Id = id; Name = name; Filter = filter; TargetCount = targetCount;
    }
    public UniverseId UniverseId { get; }
    public string Id { get; }
    public string Name { get; }
    public CatalogFilter Filter { get; }
    public long TargetCount { get; }
}
public sealed class CatalogCampaign
{
    public CatalogCampaign(UniverseId universeId, string id, string name, IEnumerable<string> objectiveIds)
    {
        ArgumentNullException.ThrowIfNull(universeId); ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(objectiveIds); var ids = objectiveIds.ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new ArgumentException("Unique objective IDs are required.");
        UniverseId = universeId; Id = id; Name = name; ObjectiveIds = Array.AsReadOnly(ids.Order(StringComparer.Ordinal).ToArray());
    }
    public UniverseId UniverseId { get; }
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<string> ObjectiveIds { get; }
}
public sealed record CatalogObjectiveProgress(CatalogObjective Objective, CatalogCoverage Coverage, CatalogStatistics Diversity);
public sealed record CatalogCampaignProgress(CatalogCampaign Campaign, IReadOnlyList<CatalogObjectiveProgress> Objectives);
public sealed record CatalogPlanningReadModel(UniverseId UniverseId, IReadOnlyList<CatalogObjectiveProgress> Objectives,
    IReadOnlyList<CatalogCampaignProgress> Campaigns);
