using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NAP.Core;

/// <summary>Reads one static PNG and returns a resized lossy WebP in memory; never writes files.</summary>
public sealed class ScenePngToWebpConverter
{
    public SceneConversionResult Convert(string sourcePath, SceneConversionSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(settings);
        using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var validation = new PngMasterValidator().Validate(stream);
        if (!validation.IsValid)
            return new(new NapIssueReport([NapIssueMapper.Map(validation, sourcePath)!]), null);

        var info = validation.ImageInfo!;
        if ((long)info.Width * info.Height > settings.MaxInputPixels)
            return Stop(NapIssueCodes.SceneInputTooLarge,
                "The scene PNG exceeds the configured pixel safety limit.", sourcePath,
                FormattableString.Invariant($"{info.Width}x{info.Height}; max_pixels={settings.MaxInputPixels}"));
        if ((long)info.Width * settings.OutputHeight != (long)info.Height * settings.OutputWidth)
            return Stop(NapIssueCodes.SceneAspectRatioMismatch,
                "The scene PNG aspect ratio does not match the configured output ratio.", sourcePath,
                FormattableString.Invariant($"source={info.Width}x{info.Height}; output={settings.OutputWidth}x{settings.OutputHeight}"));

        stream.Position = 0;
        Image<Rgba32> decoded;
        try
        {
            decoded = PngDecoder.Instance.Decode<Rgba32>(new DecoderOptions { SkipMetadata = true }, stream);
        }
        catch (ImageFormatException)
        {
            return DecodeFailed(sourcePath);
        }
        using (decoded)
        {
            if (decoded.Width != info.Width || decoded.Height != info.Height)
                return DecodeFailed(sourcePath);
            if (decoded.Width != settings.OutputWidth || decoded.Height != settings.OutputHeight)
                decoded.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(settings.OutputWidth, settings.OutputHeight),
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.Lanczos3
                }));
            using var output = new MemoryStream();
            decoded.Save(output, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = settings.WebpQuality });
            return new(new NapIssueReport([]), new SceneWebpImage(info.Width, info.Height,
                settings.OutputWidth, settings.OutputHeight, settings.WebpQuality, output.ToArray()));
        }
    }

    private static SceneConversionResult DecodeFailed(string sourcePath) =>
        Stop(NapIssueCodes.SceneDecodeFailed, "The scene PNG could not be decoded.", sourcePath);

    private static SceneConversionResult Stop(string code, string message, string sourcePath, string? detail = null) =>
        new(new NapIssueReport([new NapIssue(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, sourcePath, detail)]), null);
}
