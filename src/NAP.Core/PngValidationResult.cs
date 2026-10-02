namespace NAP.Core;

/// <summary>ImageInfo is returned only after structural validation of the whole PNG.</summary>
public sealed record PngValidationResult(
    PngValidationStatus Status,
    PngImageInfo? ImageInfo = null,
    string? Reason = null)
{
    public bool IsValid => Status == PngValidationStatus.Valid;
}
