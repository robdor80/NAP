namespace NAP.Core;

/// <summary>An immutable association between complete asset identity and exact content.</summary>
public sealed record AssetContentFingerprint
{
    public AssetContentFingerprint(UniverseAssetKey assetKey, Sha256Digest digest)
    {
        ArgumentNullException.ThrowIfNull(assetKey);
        ArgumentNullException.ThrowIfNull(digest);
        AssetKey = assetKey;
        Digest = digest;
    }

    public UniverseAssetKey AssetKey { get; }
    public Sha256Digest Digest { get; }
}
