namespace NAP.Core;

/// <summary>An image exists exactly when the conversion report is clean.</summary>
public sealed class PortraitConversionResult
{
    public PortraitConversionResult(NapIssueReport issues, PortraitWebpImage? image)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.IsClean != (image is not null))
            throw new ArgumentException("A converted image requires a clean report, and a clean report requires an image.", nameof(image));
        Issues = issues;
        Image = image;
    }

    public NapIssueReport Issues { get; }
    public PortraitWebpImage? Image { get; }
    public bool IsConverted => Image is not null;
}
