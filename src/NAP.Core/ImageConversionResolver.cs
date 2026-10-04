namespace NAP.Core;

/// <summary>Resolves declared conversion facts only; does not inspect paths or execute image processing.</summary>
public sealed class ImageConversionResolver
{
    public ResolvedImageConversion? Resolve(ValidatedAssetPackage package, long maxInputPixels)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (maxInputPixels <= 0) throw new ArgumentOutOfRangeException(nameof(maxInputPixels));
        var conversion = package.AssetRule.Conversion;
        if (conversion is null) return null;
        if (!package.FilesByRole.TryGetValue(conversion.SourceRole, out var sourcePath))
            throw new InvalidOperationException("The validated package lacks the declared conversion source role.");
        return new ResolvedImageConversion(package.AssetKey, conversion.Kind, conversion.SourceRole,
            sourcePath, conversion.OutputWidth, conversion.OutputHeight, conversion.WebpQuality, maxInputPixels);
    }
}
