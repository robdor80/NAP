namespace NAP.Core;

/// <summary>Complete asset identity: a universe plus the unchanged local asset ID.</summary>
public sealed record UniverseAssetKey
{
    public UniverseAssetKey(UniverseId universeId, string assetId)
    {
        ArgumentNullException.ThrowIfNull(universeId);
        if (!AssetNamingRules.IsValidAssetId(assetId))
        {
            throw new ArgumentException("The asset ID must follow Naming v1.", nameof(assetId));
        }
        UniverseId = universeId;
        AssetId = assetId;
    }

    public UniverseId UniverseId { get; }
    public string AssetId { get; }

    /// <summary>Diagnostic text only; never a filename or a serialization/parse contract.</summary>
    public override string ToString() => $"{UniverseId}::{AssetId}";
}
