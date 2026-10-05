namespace NAP.Core;

/// <summary>Checks exact full-frame geometry only; does not inspect paths or execute conversion.</summary>
public sealed class ImageConversionGeometryValidator
{
    public NapIssueReport Validate(PngImageInfo source, ResolvedImageConversion conversion)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(conversion);
        if (source.HasAspectRatio(conversion.OutputWidth, conversion.OutputHeight))
            return new NapIssueReport([]);
        return new NapIssueReport([new NapIssue(NapIssueCodes.ImageConversionAspectRatioMismatch,
            NapIssueSeverity.Error, NapIssueDisposition.Stop,
            "The source image aspect ratio does not match the configured output ratio; full-frame conversion is required.",
            conversion.SourcePath,
            FormattableString.Invariant($"source={source.Width}x{source.Height}; output={conversion.OutputWidth}x{conversion.OutputHeight}"))]);
    }
}
