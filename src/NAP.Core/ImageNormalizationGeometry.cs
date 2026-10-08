namespace NAP.Core;

/// <summary>Runtime limits, separate from declarative profiles. Percentages use integer basis points.</summary>
public sealed record ImageNormalizationPolicy
{
    public int MaxAddedAreaBasisPoints { get; init; } = 100;
    public int MaxAxisGrowthBasisPoints { get; init; } = 100;
    public long MaxInputPixels { get; init; } = 16_000_000;
    public long MaxCanvasPixels { get; init; } = 17_000_000;
    public long MaxFileBytes { get; init; } = 64 * 1024 * 1024;
    public long MaxPackageBytes { get; init; } = 128 * 1024 * 1024;
    public int MaxPackageEntries { get; init; } = 256;

    internal void Validate()
    {
        if (MaxAddedAreaBasisPoints is < 0 or > 500 || MaxAxisGrowthBasisPoints is < 0 or > 500 ||
            MaxInputPixels is <= 0 or > 128_000_000 || MaxCanvasPixels is <= 0 or > 128_000_000 ||
            MaxFileBytes is <= 0 or > 256 * 1024 * 1024 || MaxPackageBytes < MaxFileBytes ||
            MaxPackageBytes > 512 * 1024 * 1024 || MaxPackageEntries is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(ImageNormalizationPolicy), "Normalization limits must be bounded; growth cannot exceed 5%.");
    }
}

public sealed record ImageNormalizationGeometry(int OriginalWidth, int OriginalHeight, int CanvasWidth, int CanvasHeight,
    int Left, int Top, int Right, int Bottom)
{
    public bool HasChanges => CanvasWidth != OriginalWidth || CanvasHeight != OriginalHeight;
    public long AddedPixels => (long)CanvasWidth * CanvasHeight - (long)OriginalWidth * OriginalHeight;
    public decimal AddedPercent => 100m * AddedPixels / ((long)OriginalWidth * OriginalHeight);

    /// <summary>Smallest enclosing integer multiple of the reduced target ratio. Odd remainder goes right/bottom.</summary>
    public static ImageNormalizationGeometry Calculate(PngImageInfo source, ImageConversionRule rule, ImageNormalizationPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(rule);
        policy ??= new(); policy.Validate();
        if (source.Width <= 0 || source.Height <= 0) throw new ArgumentOutOfRangeException(nameof(source));
        var originalPixels = (long)source.Width * source.Height;
        if (originalPixels > policy.MaxInputPixels) throw ImageNormalizationException.Stop("normalization_pixel_limit", "El maestro supera el límite de píxeles.");
        var divisor = Gcd(rule.OutputWidth, rule.OutputHeight);
        var widthUnit = rule.OutputWidth / divisor; var heightUnit = rule.OutputHeight / divisor;
        var multiplier = Math.Max(((long)source.Width + widthUnit - 1) / widthUnit, ((long)source.Height + heightUnit - 1) / heightUnit);
        var width = multiplier * widthUnit; var height = multiplier * heightUnit;
        if (width > int.MaxValue || height > int.MaxValue || width * height > policy.MaxCanvasPixels)
            throw ImageNormalizationException.Stop("normalization_canvas_limit", "El lienzo mínimo excede los límites; requiere intervención artística.");
        var added = width * height - originalPixels;
        if (added * 10_000 > originalPixels * policy.MaxAddedAreaBasisPoints ||
            (width - source.Width) * 10_000 > (long)source.Width * policy.MaxAxisGrowthBasisPoints ||
            (height - source.Height) * 10_000 > (long)source.Height * policy.MaxAxisGrowthBasisPoints)
            throw ImageNormalizationException.Stop("normalization_growth_limit", "La adaptación supera el límite de crecimiento; requiere intervención artística.");
        var horizontal = (int)(width - source.Width); var vertical = (int)(height - source.Height);
        return new(source.Width, source.Height, (int)width, (int)height, horizontal / 2, vertical / 2,
            horizontal - horizontal / 2, vertical - vertical / 2);
    }

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
}

public sealed class ImageNormalizationException : InvalidOperationException
{
    private ImageNormalizationException(string code, string message, Exception? inner) : base(message, inner) => Code = code;
    public string Code { get; }
    internal static ImageNormalizationException Stop(string code, string message, Exception? inner = null) => new(code, message, inner);
}
