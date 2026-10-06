namespace NAP.Core;

/// <summary>One verified master identity in the universe-scoped archive index.</summary>
public sealed class ArchiveMasterIndexEntry
{
    public ArchiveMasterIndexEntry(string assetId, string assetType, Sha256Digest masterSha256,
        long masterSizeBytes, string relativeDirectory, bool verified)
    {
        if (!AssetNamingRules.IsValidAssetId(assetId)) throw new ArgumentException("Expected a Naming v1 asset ID.", nameof(assetId));
        if (!AssetNamingRules.IsValidMachineIdentifier(assetType)) throw new ArgumentException("Expected a machine asset type.", nameof(assetType));
        ArgumentNullException.ThrowIfNull(masterSha256);
        if (masterSizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(masterSizeBytes));
        ArchivePaths.RequireRelative(relativeDirectory);
        if (!verified) throw new ArgumentException("Only verified entries can be published.", nameof(verified));
        AssetId = assetId;
        AssetType = assetType;
        MasterSha256 = masterSha256;
        MasterSizeBytes = masterSizeBytes;
        RelativeDirectory = relativeDirectory;
        Verified = verified;
    }

    public string AssetId { get; }
    public string AssetType { get; }
    public Sha256Digest MasterSha256 { get; }
    public long MasterSizeBytes { get; }
    public string RelativeDirectory { get; }
    public bool Verified { get; }
}
