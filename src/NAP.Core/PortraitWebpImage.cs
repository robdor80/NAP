namespace NAP.Core;

/// <summary>Immutable metadata and encoded WebP bytes; callers receive defensive copies.</summary>
public sealed class PortraitWebpImage
{
    private readonly byte[] _bytes;

    internal PortraitWebpImage(int sourceWidth, int sourceHeight, int width, int height, int webpQuality, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Width = width;
        Height = height;
        WebpQuality = webpQuality;
        _bytes = (byte[])bytes.Clone();
    }

    public int SourceWidth { get; }
    public int SourceHeight { get; }
    public int Width { get; }
    public int Height { get; }
    public int WebpQuality { get; }
    public byte[] ToArray() => (byte[])_bytes.Clone();
}
