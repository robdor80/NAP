using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace NAP.Core;

/// <summary>Validates portrait output metadata and actual WebP pixels entirely in memory.</summary>
public sealed class PortraitWebpOutputValidator
{
    public NapIssueReport Validate(PortraitWebpImage image, PortraitConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        if (image.Width != settings.OutputWidth || image.Height != settings.OutputHeight || image.WebpQuality != settings.WebpQuality)
            return Stop(NapIssueCodes.PortraitOutputMetadataMismatch,
                "The portrait WebP metadata does not match the configured output.",
                FormattableString.Invariant($"image={image.Width}x{image.Height} q={image.WebpQuality}; expected={settings.OutputWidth}x{settings.OutputHeight} q={settings.WebpQuality}"));

        var bytes = image.ToArray();
        // The explicit decoder is permissive about RIFF signatures and truncated payloads.
        // Check container bounds as well as requiring a full pixel decode below.
        if (!HasCompleteWebpContainer(bytes)) return InvalidWebp();
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
                return Stop(NapIssueCodes.PortraitOutputDimensionsMismatch,
                    "The decoded portrait WebP dimensions do not match the configured output.",
                    FormattableString.Invariant($"decoded={decoded.Width}x{decoded.Height}; expected={settings.OutputWidth}x{settings.OutputHeight}"));
        }
        return new NapIssueReport([]);
    }

    private static bool HasCompleteWebpContainer(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8) ||
            (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) + 8 != bytes.Length)
            return false;
        var offset = 12;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8) return false;
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
            var chunkSize = 8L + length + (length & 1);
            if (chunkSize > bytes.Length - offset) return false;
            offset += (int)chunkSize;
        }
        return true;
    }

    private static NapIssueReport InvalidWebp() => Stop(NapIssueCodes.PortraitOutputInvalidWebp,
        "The portrait output is not a valid decodable WebP image.");

    private static NapIssueReport Stop(string code, string message, string? detail = null) =>
        new([new NapIssue(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, detail: detail)]);
}
