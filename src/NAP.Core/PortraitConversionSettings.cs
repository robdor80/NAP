namespace NAP.Core;

/// <summary>Explicit portrait output dimensions, quality and pre-decode pixel budget.</summary>
public sealed class PortraitConversionSettings
{
    public PortraitConversionSettings(int outputWidth, int outputHeight, int webpQuality, long maxInputPixels)
    {
        if (outputWidth is < 1 or > 16383) throw new ArgumentOutOfRangeException(nameof(outputWidth));
        if (outputHeight is < 1 or > 16383) throw new ArgumentOutOfRangeException(nameof(outputHeight));
        if (webpQuality is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(webpQuality));
        if (maxInputPixels <= 0) throw new ArgumentOutOfRangeException(nameof(maxInputPixels));
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        WebpQuality = webpQuality;
        MaxInputPixels = maxInputPixels;
    }

    public int OutputWidth { get; }
    public int OutputHeight { get; }
    public int WebpQuality { get; }
    public long MaxInputPixels { get; }
}
