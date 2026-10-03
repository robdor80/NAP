using System.Collections.ObjectModel;

namespace NAP.Core;

/// <summary>An immutable snapshot produced only after complete package semantic validation. Does no I/O.</summary>
public sealed class ValidatedAssetPackage
{
    private readonly AssetManifestV2 _manifest;

    internal ValidatedAssetPackage(UniverseAssetKey assetKey, string packageRoot, string manifestPath,
        AssetManifestV2 manifest, UniverseAssetRule assetRule, IReadOnlyDictionary<string, string> filesByRole)
    {
        ArgumentNullException.ThrowIfNull(assetKey);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(assetRule);
        ArgumentNullException.ThrowIfNull(filesByRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        AssetKey = assetKey;
        PackageRoot = packageRoot;
        ManifestPath = manifestPath;
        _manifest = CopyManifest(manifest);
        AssetRule = assetRule;
        FilesByRole = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(filesByRole, StringComparer.Ordinal));
    }

    public UniverseAssetKey AssetKey { get; }
    public string PackageRoot { get; }
    public string ManifestPath { get; }
    // The transport DTO has a mutable Dictionary; never expose the snapshot's dictionary.
    public AssetManifestV2 Manifest => CopyManifest(_manifest);
    public UniverseAssetRule AssetRule { get; }
    public IReadOnlyDictionary<string, string> FilesByRole { get; }

    private static AssetManifestV2 CopyManifest(AssetManifestV2 manifest) => manifest with
    { Classification = new Dictionary<string, string>(manifest.Classification, StringComparer.Ordinal) };
}
