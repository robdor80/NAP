using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace NAP.Core;

/// <summary>Validates scene output metadata and actual WebP pixels entirely in memory.</summary>
public sealed class SceneWebpOutputValidator
{
    public NapIssueReport Validate(SceneWebpImage image, SceneConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        if (image.Width != settings.OutputWidth || image.Height != settings.OutputHeight || image.WebpQuality != settings.WebpQuality)
            return Stop(NapIssueCodes.SceneOutputMetadataMismatch,
                "The scene WebP metadata does not match the configured output.",
                FormattableString.Invariant($"image={image.Width}x{image.Height} q={image.WebpQuality}; expected={settings.OutputWidth}x{settings.OutputHeight} q={settings.WebpQuality}"));

        var bytes = image.ToArray();
        // The explicit decoder is permissive about RIFF signatures and truncated payloads.
        // Check container bounds as well as requiring a full pixel decode below.
        if (!WebpContainerValidator.IsComplete(bytes)) return InvalidWebp();
        using var stream = new MemoryStream(bytes, writable: false);
        Image<Rgba32> decoded;
        try
        {
            decoded = WebpDecoder.Instance.Decode<Rgba32>(new DecoderOptions { SkipMetadata = true }, stream);
        }
        catch (ImageFormatException)
        {
            return InvalidWebp();
        }
        using (decoded)
        {
            if (decoded.Width != settings.OutputWidth || decoded.Height != settings.OutputHeight)
                return Stop(NapIssueCodes.SceneOutputDimensionsMismatch,
                    "The decoded scene WebP dimensions do not match the configured output.",
                    FormattableString.Invariant($"decoded={decoded.Width}x{decoded.Height}; expected={settings.OutputWidth}x{settings.OutputHeight}"));
        }
        return new NapIssueReport([]);
    }

    private static NapIssueReport InvalidWebp() => Stop(NapIssueCodes.SceneOutputInvalidWebp,
        "The scene output is not a valid decodable WebP image.");

    private static NapIssueReport Stop(string code, string message, string? detail = null) =>
        new([new NapIssue(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, detail: detail)]);
}
