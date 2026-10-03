using System.Collections.ObjectModel;

namespace NAP.Core;

/// <summary>Immutable validated inputs and calculated destination; not an execution graph or permission to write.</summary>
public sealed class ProcessingPlan
{
    internal ProcessingPlan(UniverseAssetKey assetKey, string assetType, string productionProfile,
        IReadOnlyDictionary<string, string> classification, string packageRoot, string manifestPath,
        IReadOnlyDictionary<string, string> filesByRole, ProductionAssetDestination productionDestination)
    {
        ArgumentNullException.ThrowIfNull(assetKey);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(filesByRole);
        ArgumentNullException.ThrowIfNull(productionDestination);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(productionProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        AssetKey = assetKey;
        AssetType = assetType;
        ProductionProfile = productionProfile;
        Classification = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(classification, StringComparer.Ordinal));
        PackageRoot = packageRoot;
        ManifestPath = manifestPath;
        FilesByRole = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(filesByRole, StringComparer.Ordinal));
        ProductionDestination = productionDestination;
    }

    public UniverseAssetKey AssetKey { get; }
    public string AssetType { get; }
    public string ProductionProfile { get; }
    public IReadOnlyDictionary<string, string> Classification { get; }
    public string PackageRoot { get; }
    public string ManifestPath { get; }
    public IReadOnlyDictionary<string, string> FilesByRole { get; }
    public ProductionAssetDestination ProductionDestination { get; }
}
