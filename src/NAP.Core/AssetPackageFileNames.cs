namespace NAP.Core;

/// <summary>Derives canonical filenames from a valid asset ID, without paths or filesystem operations.</summary>
public sealed class AssetPackageFileNames
{
    public AssetPackageFileNames(string assetId)
    {
        if (!AssetNamingRules.IsValidAssetId(assetId))
        {
            throw new ArgumentException("The asset ID must follow Naming v1.", nameof(assetId));
        }
        AssetId = assetId;
    }

    public string AssetId { get; }
    public string Zip => AssetId + ".zip";
    public string MasterPng => AssetId + ".png";
    public string Prompt => AssetId + "_prompt.md";
    public string Info => AssetId + "_info.md";
    public string Manifest => AssetId + "_manifest.json";
    public string VisualIdentity => AssetId + "_visual_identity.json";
    public string ProductionWebP => AssetId + ".webp";
}
