namespace NAP.Core;

/// <summary>A read-only point-in-time plan. Execution revalidates it under the archive lock.</summary>
public sealed class ArchiveMasterPlan
{
    internal ArchiveMasterPlan(UniverseAssetKey assetKey, string assetType, string archiveRoot,
        string relativeDirectory, string destinationDirectory, Sha256Digest masterDigest, long masterSizeBytes,
        IEnumerable<ArchiveMasterFile> files, IEnumerable<ArchiveMasterFile> filesToCopy, NapIssueReport issues, ArchiveMasterAction action)
    {
        ArgumentNullException.ThrowIfNull(assetKey);
        ArgumentNullException.ThrowIfNull(masterDigest);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(filesToCopy);
        ArgumentNullException.ThrowIfNull(issues);
        if (!AssetNamingRules.IsValidMachineIdentifier(assetType)) throw new ArgumentException("Expected a machine asset type.", nameof(assetType));
        ArchivePaths.RequireRelative(relativeDirectory);
        if (masterSizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(masterSizeBytes));
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        var snapshot = files.ToArray();
        var missing = filesToCopy.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(file => file is null) || missing.Any(file => file is null || !snapshot.Contains(file)))
            throw new ArgumentException("Expected an exact package file snapshot and subset of missing files.");
        AssetKey = assetKey;
        AssetType = assetType;
        ArchiveRoot = archiveRoot;
        RelativeDirectory = relativeDirectory;
        DestinationDirectory = destinationDirectory;
        MasterDigest = masterDigest;
        MasterSizeBytes = masterSizeBytes;
        Files = Array.AsReadOnly(snapshot);
        FilesToCopy = Array.AsReadOnly(missing);
        Issues = issues;
        Action = action;
    }

    public UniverseAssetKey AssetKey { get; }
    public string AssetType { get; }
    public string ArchiveRoot { get; }
    public string RelativeDirectory { get; }
    public string DestinationDirectory { get; }
    public Sha256Digest MasterDigest { get; }
    public long MasterSizeBytes { get; }
    public IReadOnlyList<ArchiveMasterFile> Files { get; }
    public IReadOnlyList<ArchiveMasterFile> FilesToCopy { get; }
    public NapIssueReport Issues { get; }
    public ArchiveMasterAction Action { get; }
}
