namespace NAP.Core;

/// <summary>A calculated asset directory under an authorized production root, not permission to write.</summary>
public sealed class ProductionAssetDestination
{
    internal ProductionAssetDestination(UniverseAssetKey assetKey, string rootPath,
        string relativeDirectory, string fullDirectoryPath)
    {
        ArgumentNullException.ThrowIfNull(assetKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullDirectoryPath);
        AssetKey = assetKey;
        RootPath = rootPath;
        RelativeDirectory = relativeDirectory;
        FullDirectoryPath = fullDirectoryPath;
    }

    public UniverseAssetKey AssetKey { get; }
    public string RootPath { get; }
    public string RelativeDirectory { get; }
    public string FullDirectoryPath { get; }
}
