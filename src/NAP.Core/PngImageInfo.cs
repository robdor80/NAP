namespace NAP.Core;

/// <summary>IHDR metadata; does not imply that the pixels have been decoded.</summary>
public sealed record PngImageInfo(int Width, int Height, byte BitDepth, byte ColorType, byte InterlaceMethod)
{
    /// <summary>Compares an exact positive ratio without floating point or 32-bit overflow.</summary>
    public bool HasAspectRatio(int widthUnits, int heightUnits) =>
        Width > 0 && Height > 0 && widthUnits > 0 && heightUnits > 0 &&
        (long)Width * heightUnits == (long)Height * widthUnits;
}
