namespace NAP.Core;

/// <summary>Immutable conversion facts resolved from a validated package, with an explicit runtime pixel budget.</summary>
public sealed class ResolvedImageConversion
{
    internal ResolvedImageConversion(UniverseAssetKey assetKey, ImageConversionKind kind, string sourceRole,
        string sourcePath, int outputWidth, int outputHeight, int webpQuality, long maxInputPixels)
    {
        AssetKey = assetKey;
        Kind = kind;
        SourceRole = sourceRole;
        SourcePath = sourcePath;
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        WebpQuality = webpQuality;
        MaxInputPixels = maxInputPixels;
    }

    public UniverseAssetKey AssetKey { get; }
    public ImageConversionKind Kind { get; }
    public string SourceRole { get; }
    public string SourcePath { get; }
    public int OutputWidth { get; }
    public int OutputHeight { get; }
    public int WebpQuality { get; }
    public long MaxInputPixels { get; }
}
