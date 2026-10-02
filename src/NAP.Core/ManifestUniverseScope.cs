namespace NAP.Core;

/// <summary>Checks only the universe boundary of a Manifest v2 DTO against an explicit active context.</summary>
public static class ManifestUniverseScope
{
    public static bool Matches(AssetManifestV2 manifest, UniverseContext context)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(context);
        return new UniverseId(manifest.UniverseId) == context.Id;
    }
}
